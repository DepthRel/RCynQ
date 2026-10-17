using System.Buffers.Text;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Delivers an enrollment the way the node at the end of the chain takes it: to its own TLS port through
/// <see cref="PeerLinkFactory"/>, or to its SSH server with the one-time key of the ticket, where the forced command
/// <c>rcynq enroll</c> passes it on. The host keys and the certificate are pinned from the cards before anything is sent.
/// </summary>
public sealed class EnrollmentChannel : IEnrollmentChannel
{
    /// <summary>How long an enrollment may take, the SSH connections through the Relays included.</summary>
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(2);

    /// <summary>The JSON options of the exchange.</summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The opener of links to a TLS port.</summary>
    private readonly PeerLinkFactory _links;

    /// <summary>The SSH client.</summary>
    private readonly ISshExecutor _ssh;

    /// <summary>The guard that pins the host keys of the cards, or <see langword="null"/>.</summary>
    private readonly IPinnedHostKeyGuard? _hostKeys;

    /// <summary>The data folder, where the one-time key lives for the moment of the connection.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrollmentChannel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="links">The opener of links to a TLS port.</param>
    /// <param name="ssh">The SSH client.</param>
    /// <param name="appData">The data folder.</param>
    /// <param name="hostKeys">The guard that pins the host keys, or <see langword="null"/>.</param>
    public EnrollmentChannel(ILocalizer localizer, PeerLinkFactory links, ISshExecutor ssh, IAppDataFactory appData, IPinnedHostKeyGuard? hostKeys = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _ssh = ssh ?? throw new ArgumentNullException(nameof(ssh));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _hostKeys = hostKeys;
    }

    /// <inheritdoc />
    public async Task<EnrollmentResponse> SendAsync(IReadOnlyList<ChainLinkConfig> chain, InvitationTicket ticket, EnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(request);

        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(chain);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            return route.Endpoint.Peer.EndpointMode == EndpointMode.Direct
                ? await SendToTlsPortAsync(route, request, timeout.Token)
                : await SendToSshServerAsync(route, ticket, request, timeout.Token);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SyncBridgeException(_localizer.Format("Join.Timeout", route.Endpoint.Peer.Id.Value), ex);
        }
    }

    /// <summary>
    /// Posts the enrollment to the TLS port of the node, straight or through the SSH forward of the last Relay.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="request">The enrollment.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer.</returns>
    private async Task<EnrollmentResponse> SendToTlsPortAsync(ChainRoute route, EnrollmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await using var link = await _links.OpenAsync(route, cancellationToken);
            using var message = link.CreateRequest(HttpMethod.Post, ApiRoutes.PEER_ENROLL);
            message.Content = JsonContent.Create(request, options: _json);
            if (request.Language is { } language)
            {
                message.Headers.AcceptLanguage.ParseAdd(language);
            }

            using var response = await link.Client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse(route, body);
        }
        catch (HttpRequestException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Join.Unreachable", route.Endpoint.Peer.Id.Value, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Logs in to the SSH server of the node with the one-time key and hands it the enrollment as the command, which the
    /// forced command of the key reads.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="ticket">The ticket with the key.</param>
    /// <param name="request">The enrollment.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer.</returns>
    private async Task<EnrollmentResponse> SendToSshServerAsync(ChainRoute route, InvitationTicket ticket, EnrollmentRequest request, CancellationToken cancellationToken)
    {
        if (ticket.EnrollmentKey is null)
        {
            throw new SyncBridgeException(_localizer.Format("Join.NoKey", route.Endpoint.Peer.Id.Value));
        }

        if (_hostKeys is not null)
        {
            await _hostKeys.EnsurePinnedAsync(route.SshPeers, cancellationToken);
        }

        var folder = Path.Combine(_appData.Create().KeysDirectory, "join-" + ticket.Id.ToString("N"));
        var keyFile = Path.Combine(folder, "key");
        try
        {
            Directory.CreateDirectory(folder);
            FilePermissions.RestrictDirectoryToOwner(folder);
            await File.WriteAllTextAsync(keyFile, ticket.EnrollmentKey.Replace("\r\n", "\n", StringComparison.Ordinal), cancellationToken);
            FilePermissions.RestrictFileToOwner(keyFile);

            var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request, _json)));
            var result = await _ssh.ExecuteWithKeyAsync(route.Target!, route.Jumps, keyFile, payload, cancellationToken);
            if (result.ExitCode != 0)
            {
                throw new SyncBridgeException(_localizer.Format("Join.SshRefused", route.Endpoint.Peer.Id.Value, result.StandardError.Trim()));
            }

            return Parse(route, result.StandardOutput);
        }
        catch (SshExecutionException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Join.Unreachable", route.Endpoint.Peer.Id.Value, _localizer.Describe(ex)), ex);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// Reads the answer of the node.
    /// </summary>
    /// <param name="route">The route, whose node is named in the message.</param>
    /// <param name="body">The JSON text.</param>
    /// <returns>The answer.</returns>
    /// <exception cref="SyncBridgeException">The answer is not an enrollment answer.</exception>
    private EnrollmentResponse Parse(ChainRoute route, string body)
    {
        try
        {
            var response = JsonSerializer.Deserialize<EnrollmentResponse>(body, _json);
            if (response?.Message is not null)
            {
                return response;
            }

            // The service answers a request it could not take with an error of its own.
            var error = JsonSerializer.Deserialize<ErrorResponse>(body, _json);
            if (error?.Error is { Length: > 0 } text)
            {
                return new EnrollmentResponse(false, text);
            }
        }
        catch (JsonException)
        {
            // Not JSON at all: the refusal below names the node.
        }

        throw new SyncBridgeException(_localizer.Format("Join.BadAnswer", route.Endpoint.Peer.Id.Value));
    }
}
