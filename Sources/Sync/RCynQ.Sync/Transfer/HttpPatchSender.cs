using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Endpoint;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// <see cref="IPatchSender"/> that posts envelopes to the peer's loopback endpoint through an SSH local forward.
/// </summary>
/// <remarks>
/// <para>
/// Flow: validate the envelope and chain, sign, start <c>ssh -N -L</c> (with <c>-J</c> through the mediators),
/// <c>POST http://127.0.0.1:&lt;forward&gt;/patch</c> with <c>X-Node-Id</c> and <c>X-Link-Token</c>, parse the
/// <see cref="PatchApplyResult"/>, stop the forward.
/// </para>
/// <para>
/// Network failures and timeouts are retried once. If the retry is refused as a replay, the first attempt reached the
/// peer and only its response was lost; the result is reported as <see cref="PatchStatus.Pending"/>.
/// </para>
/// </remarks>
public sealed class HttpPatchSender : IPatchSender, IDisposable
{
    /// <summary>
    /// The sender node header.
    /// </summary>
    public const string NodeIdHeader = "X-Node-Id";

    /// <summary>
    /// The link token header.
    /// </summary>
    public const string LinkTokenHeader = "X-Link-Token";

    /// <summary>
    /// JSON options for responses.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The HTTP client.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The forward manager.
    /// </summary>
    private readonly ILocalForwardManager _forwards;

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
    /// Creates a handler that accepts only the certificate with the given pinned fingerprint.
    /// </summary>
    private readonly Func<string, HttpMessageHandler> _pinnedHandlers;

    /// <summary>
    /// The clients per pinned fingerprint.
    /// </summary>
    private readonly Dictionary<string, HttpClient> _pinnedClients = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="HttpPatchSender"/> class.
    /// </summary>
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
    public HttpPatchSender(
        HttpMessageHandler handler,
        ILocalForwardManager forwards,
        ISignatureService signatures,
        PatchEnvelopeSerializer serializer,
        LinkTokenProvider tokens,
        NodeConfig node,
        HttpPatchSenderOptions options,
        Func<string, HttpMessageHandler>? pinnedHandlers = null)
    {
        _pinnedHandlers = pinnedHandlers ?? PinnedHandler;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _forwards = forwards;
        _signatures = signatures;
        _serializer = serializer;
        _tokens = tokens;
        _node = node;
        _options = options;
    }

    /// <summary>
    /// Gets the client for a destination: the shared one for loopback endpoints, a pinned one per fingerprint.
    /// </summary>
    /// <param name="tlsFingerprint">The pinned fingerprint, or <see langword="null"/>.</param>
    /// <returns>The client.</returns>
    private HttpClient Client(string? tlsFingerprint)
    {
        if (tlsFingerprint is null)
        {
            return _http;
        }

        lock (_pinnedClients)
        {
            if (!_pinnedClients.TryGetValue(tlsFingerprint, out var client))
            {
                client = new HttpClient(_pinnedHandlers(tlsFingerprint)) { Timeout = Timeout.InfiniteTimeSpan };
                _pinnedClients[tlsFingerprint] = client;
            }

            return client;
        }
    }

    /// <summary>
    /// Creates a handler that accepts exactly one certificate and ignores the certificate authorities of the system.
    /// </summary>
    /// <param name="tlsFingerprint">The pinned fingerprint.</param>
    /// <returns>The handler.</returns>
    /// <remarks>
    /// The peer signs its own certificate, so the usual chain and name checks say nothing. What matters is that the
    /// certificate is the one published in the peer's export file, which is what this check compares.
    /// </remarks>
    private static HttpMessageHandler PinnedHandler(string tlsFingerprint)
        => new SocketsHttpHandler
        {
            UseProxy = false,
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null
                    && NodeCertificateStore.Matches(new X509Certificate2(certificate), tlsFingerprint)
            }
        };

    /// <inheritdoc />
    public async Task<PatchApplyResult> SendAsync(PatchEnvelope envelope, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
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
            throw new SyncBridgeException("The link token is not configured; set SyncBridge:LinkToken or the 'link-token' secret.");
        }

        var body = _serializer.Serialize(envelope);

        // A peer without an SSH server is reached on its own TLS port: through a forward that stops at the mediator,
        // or straight over the network when there is no mediator.
        var port = route.UsesSsh
            ? await _forwards.StartAsync(route.Target!, route.Jumps, route.Endpoint.LocalPort, route.RemoteHost, route.RemotePort, cancellationToken)
            : route.RemotePort;
        var host = route.UsesSsh ? "127.0.0.1" : route.RemoteHost;
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await PostAsync(host, port, route.TlsFingerprint, body, token, attempt, envelope.Id, cancellationToken);
                }
                catch (Exception ex) when (attempt == 1 && IsTransient(ex, cancellationToken))
                {
                    await Task.Delay(_options.RetryDelay, cancellationToken);
                }
            }
        }
        catch (Exception ex) when (IsTransient(ex, cancellationToken))
        {
            var destination = route.UsesTls ? $"{route.Endpoint.Peer.Id} ({route.RemoteHost}:{route.RemotePort})" : $"{route.Target}";
            var hint = route.UsesTls && ex.GetBaseException() is System.Security.Authentication.AuthenticationException
                ? " The certificate presented by the peer does not match the fingerprint from its export file; import the peer's current export."
                : string.Empty;
            throw new SyncBridgeException($"Patch {envelope.Id} could not be delivered to {destination}: {ex.Message}{hint}", ex);
        }
        finally
        {
            if (route.UsesSsh)
            {
                await _forwards.StopAsync(port, CancellationToken.None);
            }
        }
    }

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose()
    {
        _http.Dispose();
        lock (_pinnedClients)
        {
            foreach (var client in _pinnedClients.Values)
            {
                client.Dispose();
            }

            _pinnedClients.Clear();
        }
    }

    /// <summary>
    /// Performs one POST attempt.
    /// </summary>
    /// <param name="port">The local forward port.</param>
    /// <param name="body">The JSON body.</param>
    /// <param name="token">The link token.</param>
    /// <param name="attempt">The attempt number.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The peer's result.</returns>
    /// <exception cref="SyncBridgeException">The peer refused the patch or answered with an invalid body.</exception>
    private async Task<PatchApplyResult> PostAsync(
        string host,
        int port,
        string? tlsFingerprint,
        byte[] body,
        string token,
        int attempt,
        Guid patchId,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.RequestTimeout);
        var scheme = tlsFingerprint is null ? "http" : "https";
        var authority = host.Contains(':', StringComparison.Ordinal) && !host.StartsWith('[') ? $"[{host}]" : host;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{scheme}://{authority}:{port}/patch") { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        request.Headers.Add(NodeIdHeader, _node.Id.Value);
        request.Headers.Add(LinkTokenHeader, token);

        using var response = await Client(tlsFingerprint).SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(timeout.Token);
        if (response.IsSuccessStatusCode)
        {
            try
            {
                return JsonSerializer.Deserialize<PatchApplyResult>(text, JsonOptions)
                    ?? throw new SyncBridgeException($"The peer returned an empty result for patch {patchId}.");
            }
            catch (JsonException ex)
            {
                throw new SyncBridgeException($"The peer returned an invalid result for patch {patchId}: {ex.Message}", ex);
            }
        }

        var rejection = TryParseRejection(text);
        if (attempt > 1 && rejection?.Reason == PatchRejectionReason.Replay)
        {
            return new PatchApplyResult(patchId, PatchStatus.Pending, 0, Array.Empty<ConflictFile>(),
                "The peer already received this patch but the first response was lost; check the patch status on the peer.");
        }

        var reason = rejection is null ? text : $"{rejection.Reason}: {rejection.Error}";
        throw new SyncBridgeException($"The peer refused patch {patchId} with HTTP {(int)response.StatusCode}: {reason}".Trim());
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
            return string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<PatchRejectionResponse>(text, JsonOptions);
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
}
