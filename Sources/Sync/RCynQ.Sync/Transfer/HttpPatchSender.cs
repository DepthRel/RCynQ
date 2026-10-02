using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Localization;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// <see cref="IPatchSender"/> that delivers a patch to the peer over HTTP as a session, one file per request.
/// </summary>
/// <remarks>
/// <para>
/// Flow: validate the envelope and chain, sign, start <c>ssh -N -L</c> (with <c>-J</c> through the relays) when the peer is
/// reached through SSH, then (1) <c>POST /patch/sessions</c> with the gzip-compressed signed manifest, which answers with the hashes of
/// the content the peer still lacks, (2) <c>PUT /patch/sessions/{id}/content/{sha256}</c> once per missing file with the gzip-compressed
/// content streamed from disk, (3) <c>POST /patch/sessions/{id}/commit</c>, which applies the patch on the peer. Finally the forward is stopped.
/// </para>
/// <para>
/// Every request carries <c>X-Node-Id</c> and <c>X-Link-Token</c>. A network failure or timeout of a request is retried once;
/// files the peer already received are not sent again, also not by a later attempt. If the retry of the commit is refused as a replay,
/// the first attempt reached the peer and only its response was lost; the result is reported as <see cref="PatchStatus.Pending"/>.
/// </para>
/// </remarks>
public sealed class HttpPatchSender : IPatchSender, IDisposable
{
    /// <summary>
    /// The sender node header.
    /// </summary>
    public const string NODE_ID_HEADER = ApiHeaders.NODE_ID;

    /// <summary>
    /// The link token header.
    /// </summary>
    public const string LINK_TOKEN_HEADER = ApiHeaders.LINK_TOKEN;

    /// <summary>
    /// JSON options for responses.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Opens the connections to the peer.
    /// </summary>
    private readonly PeerLinkFactory _links;

    /// <summary>
    /// The signature service.
    /// </summary>
    private readonly ISignatureService _signatures;

    /// <summary>
    /// The envelope serializer.
    /// </summary>
    private readonly PatchEnvelopeSerializer _serializer;

    /// <summary>
    /// The link token provider.
    /// </summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>
    /// The local node.
    /// </summary>
    private readonly NodeConfig _node;

    /// <summary>
    /// The options.
    /// </summary>
    private readonly HttpPatchSenderOptions _options;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpPatchSender"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="handler">The HTTP handler; production code uses a handler with the system proxy disabled.</param>
    /// <param name="forwards">The forward manager.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="serializer">The envelope serializer.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The options.</param>
    /// <param name="pinnedHandlers">
    /// Creates the handler used for a peer that accepts connections on its own TLS port, given its pinned
    /// fingerprint; <see langword="null"/> uses a handler that trusts that certificate and no other.
    /// </param>
    /// <param name="hostKeys">
    /// Holds SSH connections to the pinned host keys of the peers, or <see langword="null"/> to check nothing.
    /// </param>
    public HttpPatchSender(
        ILocalizer localizer,
        HttpMessageHandler handler,
        ILocalForwardManager forwards,
        ISignatureService signatures,
        PatchEnvelopeSerializer serializer,
        LinkTokenProvider tokens,
        NodeConfig node,
        HttpPatchSenderOptions options,
        Func<string, HttpMessageHandler>? pinnedHandlers = null,
        IPinnedHostKeyGuard? hostKeys = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = new PeerLinkFactory(handler, forwards, pinnedHandlers, hostKeys);
        _signatures = signatures;
        _serializer = serializer;
        _tokens = tokens;
        _node = node;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> SendAsync(PatchEnvelope envelope, IPatchContent content, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(chain);
        if (envelope.SourceNode != _node.Id)
        {
            throw new ArgumentException($"Envelope sender {envelope.SourceNode} is not the local node {_node.Id}.", nameof(envelope));
        }

        var route = ChainRoute.Resolve(chain);
        if (route.Endpoint.Peer.Id != envelope.TargetNode)
        {
            throw new ArgumentException($"Envelope receiver {envelope.TargetNode} is not the chain endpoint {route.Endpoint.Peer.Id}.", nameof(envelope));
        }

        if (!envelope.IsSigned)
        {
            envelope = envelope.WithSignature(await _signatures.SignAsync(PayloadCanonicalizer.Build(envelope), cancellationToken));
        }

        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new SyncBridgeException(_localizer.Get("Send.NoToken"));
        }

        var uploads = UploadsOf(envelope);
        var manifestBody = GzipCodec.Compress(_serializer.Serialize(envelope));

        // A peer without an SSH server is reached on its own TLS port: through a forward that stops at the relay,
        // or straight over the network when there is no relay. Disposing the link stops the forward.
        await using var link = await _links.OpenAsync(route, cancellationToken);
        var peer = new Peer(link, token);
        try
        {
            var session = await WithRetryAsync(
                (attempt, ct) => OpenSessionAsync(peer, envelope, manifestBody, ct),
                cancellationToken);
            foreach (var hash in session.MissingContent)
            {
                if (!uploads.TryGetValue(hash, out var change))
                {
                    throw new SyncBridgeException(_localizer.Format("Send.UnknownContent", envelope.Id, hash));
                }

                await WithRetryAsync(
                    async (attempt, ct) =>
                    {
                        await UploadAsync(peer, envelope, change, content, ct);
                        return true;
                    },
                    cancellationToken);
            }

            return await WithRetryAsync(
                (attempt, ct) => CommitAsync(peer, envelope.Id, attempt, ct),
                cancellationToken);
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            var destination = route.UsesTls ? $"{route.Endpoint.Peer.Id} ({route.RemoteHost}:{route.RemotePort})" : $"{route.Target}";
            var hint = route.UsesTls && ex.GetBaseException() is System.Security.Authentication.AuthenticationException
                ? _localizer.Get("Send.CertificateMismatch")
                : string.Empty;
            throw new SyncBridgeException(_localizer.Format("Send.NotDelivered", envelope.Id, destination, _localizer.Describe(ex), hint), ex);
        }
    }

    /// <summary>
    /// Disposes the HTTP clients.
    /// </summary>
    public void Dispose() => _links.Dispose();

    /// <summary>
    /// Finds the file to upload for each hash of the manifest and rejects files that are too large.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The first manifest entry of every distinct hash.</returns>
    /// <exception cref="SyncBridgeException">A file is larger than the limit of one upload.</exception>
    private Dictionary<string, FileChange> UploadsOf(PatchEnvelope envelope)
    {
        var uploads = new Dictionary<string, FileChange>(StringComparer.Ordinal);
        foreach (var change in envelope.Manifest.Where(change => change.Kind != ChangeKind.Deleted && change.Sha256 is not null))
        {
            if (change.SizeBytes > _options.MaxFileBytes)
            {
                throw new SyncBridgeException(_localizer.Format("Send.FileTooLarge", change.Path, change.SizeBytes / (1024 * 1024), _options.MaxFileBytes / (1024 * 1024)));
            }

            uploads.TryAdd(change.Sha256!, change);
        }

        return uploads;
    }

    /// <summary>
    /// Opens the session with the signed manifest.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="envelope">The signed envelope.</param>
    /// <param name="manifestBody">The compressed manifest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session with the content the peer lacks.</returns>
    /// <exception cref="SyncBridgeException">The peer refused the manifest or answered with an invalid body.</exception>
    private async Task<PatchSession> OpenSessionAsync(Peer peer, PatchEnvelope envelope, byte[] manifestBody, CancellationToken cancellationToken)
    {
        using var request = peer.Request(HttpMethod.Post, ApiRoutes.PATCH_SESSIONS, _node.Id.Value);
        request.Content = new ByteArrayContent(manifestBody);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Content.Headers.ContentEncoding.Add(GzipCodec.CONTENT_ENCODING);

        var (response, text) = await SendRequestAsync(peer, request, cancellationToken);
        using (response)
        {
            // A peer of an older version answers the unknown route with an empty or a plain "route not found" 404,
            // never with the rejection body a current peer sends.
            if (response.StatusCode == HttpStatusCode.NotFound && !IsRejectionBody(text))
            {
                throw new SyncBridgeException(_localizer.Format("Send.OlderPeer", envelope.Id));
            }

            if (!response.IsSuccessStatusCode)
            {
                throw Refusal(envelope.Id, response, text);
            }

            return Parse<PatchSession>(text, envelope.Id);
        }
    }

    /// <summary>
    /// Uploads the content of one file.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="envelope">The envelope.</param>
    /// <param name="change">The manifest entry whose content is uploaded.</param>
    /// <param name="content">The source of the content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the peer stored the file.</returns>
    /// <exception cref="SyncBridgeException">The peer refused the content.</exception>
    private async Task UploadAsync(Peer peer, PatchEnvelope envelope, FileChange change, IPatchContent content, CancellationToken cancellationToken)
    {
        using var request = peer.Request(HttpMethod.Put, ApiRoutes.PatchSessionContentPath(envelope.Id, change.Sha256!), _node.Id.Value);

        // The stream is opened per attempt: a retry has to start reading from the beginning.
        request.Content = new GzipStreamContent(await content.OpenReadAsync(change, cancellationToken));
        var (response, text) = await SendRequestAsync(peer, request, cancellationToken);
        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw Refusal(envelope.Id, response, text);
            }
        }
    }

    /// <summary>
    /// Asks the peer to apply the patch.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="attempt">The attempt number.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result of the peer.</returns>
    /// <exception cref="SyncBridgeException">The peer refused the request or answered with an invalid body.</exception>
    private async Task<PatchApplyResult> CommitAsync(Peer peer, Guid patchId, int attempt, CancellationToken cancellationToken)
    {
        using var request = peer.Request(HttpMethod.Post, ApiRoutes.PatchSessionCommitPath(patchId), _node.Id.Value);
        var (response, text) = await SendRequestAsync(peer, request, cancellationToken);
        using (response)
        {
            if (response.IsSuccessStatusCode)
            {
                return Parse<PatchApplyResult>(text, patchId);
            }

            var rejection = TryParseRejection(text);
            if (attempt > 1 && rejection?.Reason == PatchRejectionReason.Replay)
            {
                return new PatchApplyResult(patchId, PatchStatus.Pending, 0, Array.Empty<ConflictFile>(), _localizer.Get("Send.ResponseLost"));
            }

            throw Refusal(patchId, response, text);
        }
    }

    /// <summary>
    /// Sends a request with the request timeout and reads the whole response.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The response and its body text.</returns>
    private async Task<(HttpResponseMessage Response, string Text)> SendRequestAsync(Peer peer, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        var response = await peer.Client.SendAsync(request, timeout.Token);
        try
        {
            return (response, await response.Content.ReadAsStringAsync(timeout.Token));
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs a request and retries it once after a network failure.
    /// </summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="action">The request; it receives the attempt number.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    private async Task<T> WithRetryAsync<T>(Func<int, CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await action(attempt, cancellationToken);
            }
            catch (Exception ex) when (attempt == 1 && IsTransient(ex, cancellationToken))
            {
                await Task.Delay(_options.RetryDelay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// Builds the error for a refused request.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="response">The response.</param>
    /// <param name="text">The body text.</param>
    /// <returns>The exception.</returns>
    private SyncBridgeException Refusal(Guid patchId, HttpResponseMessage response, string text)
    {
        var rejection = TryParseRejection(text);
        var reason = rejection is null ? text : $"{rejection.Reason}: {rejection.Error}";
        return new SyncBridgeException(_localizer.Format("Send.Refused", patchId, (int)response.StatusCode, reason).Trim());
    }

    /// <summary>
    /// Parses a successful response.
    /// </summary>
    /// <typeparam name="T">The body type.</typeparam>
    /// <param name="text">The body text.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <returns>The body.</returns>
    /// <exception cref="SyncBridgeException">The body is empty or invalid.</exception>
    private T Parse<T>(string text, Guid patchId)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, _jsonOptions)
                ?? throw new SyncBridgeException(_localizer.Format("Send.EmptyResult", patchId));
        }
        catch (JsonException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Send.InvalidResult", patchId, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Checks whether a response body is the rejection a patch endpoint answers with, and not a generic error.
    /// </summary>
    /// <param name="text">The body.</param>
    /// <returns><see langword="true"/> when the body is JSON with a <c>reason</c> property.</returns>
    private static bool IsRejectionBody(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.EnumerateObject().Any(property => string.Equals(property.Name, "reason", StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Parses a rejection body.
    /// </summary>
    /// <param name="text">The body.</param>
    /// <returns>The rejection, or <see langword="null"/>.</returns>
    private static PatchRejectionResponse? TryParseRejection(string text)
    {
        try
        {
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<PatchRejectionResponse>(text, _jsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Checks whether a failure is a transient network failure (and not caller cancellation).
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    /// <returns><see langword="true"/> for transient failures.</returns>
    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
        => exception is HttpRequestException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>
    /// The connection to the peer and the link token for one send.
    /// </summary>
    /// <param name="Link">The connection.</param>
    /// <param name="Token">The link token.</param>
    private sealed record Peer(PeerLink Link, string Token)
    {
        /// <summary>
        /// Gets the HTTP client.
        /// </summary>
        public HttpClient Client => Link.Client;

        /// <summary>
        /// Creates a request with the authentication headers.
        /// </summary>
        /// <param name="method">The HTTP method.</param>
        /// <param name="path">The path.</param>
        /// <param name="nodeId">The identifier of the sending node.</param>
        /// <returns>The request.</returns>
        public HttpRequestMessage Request(HttpMethod method, string path, string nodeId) => Link.CreateRequest(method, path, nodeId, Token);
    }
}
