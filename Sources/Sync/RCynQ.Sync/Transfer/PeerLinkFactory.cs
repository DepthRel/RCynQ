using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Exceptions;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Endpoint;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Opens connections to the counterpart endpoint of the chain and hands out ready-made HTTP clients for them.
/// </summary>
/// <remarks>
/// A peer reached through SSH is served through a local forward that is started for the lifetime of a <see cref="PeerLink"/> and
/// stopped when it is disposed. A peer without an SSH server is reached on its own TLS port, through a forward that stops at the
/// mediator or straight over the network; its certificate is pinned by the fingerprint from its export file.
/// </remarks>
public sealed class PeerLinkFactory : IDisposable
{
    /// <summary>
    /// The HTTP client for loopback endpoints.
    /// </summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The forward manager.
    /// </summary>
    private readonly ILocalForwardManager _forwards;

    /// <summary>
    /// Creates a handler that accepts only the certificate with the given pinned fingerprint.
    /// </summary>
    private readonly Func<string, HttpMessageHandler> _pinnedHandlers;

    /// <summary>
    /// The clients per pinned fingerprint.
    /// </summary>
    private readonly Dictionary<string, HttpClient> _pinnedClients = new(StringComparer.Ordinal);

    /// <summary>
    /// Holds SSH connections to the pinned host keys of the peers, or <see langword="null"/> when nothing is checked.
    /// </summary>
    private readonly IPinnedHostKeyGuard? _hostKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerLinkFactory"/> class.
    /// </summary>
    /// <param name="handler">The HTTP handler for loopback endpoints; production code uses a handler with the system proxy disabled.</param>
    /// <param name="forwards">The forward manager.</param>
    /// <param name="pinnedHandlers">
    /// Creates the handler used for a peer that accepts connections on its own TLS port, given its pinned fingerprint;
    /// <see langword="null"/> uses a handler that trusts that certificate and no other.
    /// </param>
    /// <param name="hostKeys">
    /// Holds SSH connections to the pinned host keys of the peers, or <see langword="null"/> to check nothing.
    /// </param>
    public PeerLinkFactory(HttpMessageHandler handler, ILocalForwardManager forwards, Func<string, HttpMessageHandler>? pinnedHandlers = null, IPinnedHostKeyGuard? hostKeys = null)
    {
        _hostKeys = hostKeys;
        _http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        _forwards = forwards;
        _pinnedHandlers = pinnedHandlers ?? PinnedHandler;
    }

    /// <summary>
    /// Opens the connection to the endpoint of a route, starting the SSH forward when the route needs one.
    /// </summary>
    /// <param name="route">The resolved route.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The link; the caller disposes it to stop the forward.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="route"/> is <see langword="null"/>.</exception>
    /// <exception cref="SshExecutionException">
    /// A host on the way does not own the host key pinned for it, in which case no forward is started and nothing is
    /// sent, or the forward could not be started.
    /// </exception>
    public async Task<PeerLink> OpenAsync(ChainRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var port = route.UsesSsh ? await StartVerifiedForwardAsync(route, cancellationToken) : route.RemotePort;
        return new PeerLink(
            route,
            Client(route.TlsFingerprint),
            route.UsesTls ? "https" : "http",
            route.UsesSsh ? "127.0.0.1" : route.RemoteHost,
            port,
            route.UsesSsh ? () => _forwards.StopAsync(port, CancellationToken.None) : null);
    }

    /// <summary>
    /// Starts the SSH forward of a route, after the hosts it passes are known by their pinned host keys.
    /// </summary>
    /// <param name="route">The resolved route.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The local port of the forward.</returns>
    private async Task<int> StartVerifiedForwardAsync(ChainRoute route, CancellationToken cancellationToken)
    {
        // Before the forward: once the pinned keys are the only ones on record, the SSH client that opens the forward
        // refuses any other key by itself, so the link token and the patch never reach a host that is not the pinned one.
        if (_hostKeys is not null)
        {
            await _hostKeys.EnsurePinnedAsync(route.SshPeers, cancellationToken);
        }

        return await _forwards.StartAsync(route.Target!, route.Jumps, route.Endpoint.LocalPort, route.RemoteHost, route.RemotePort, cancellationToken);
    }

    /// <summary>
    /// Disposes the HTTP clients.
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
                RemoteCertificateValidationCallback = (_, certificate, _, _) => IsPinned(certificate, tlsFingerprint)
            }
        };

    /// <summary>
    /// Checks the certificate a peer presented against the pinned fingerprint.
    /// </summary>
    /// <param name="certificate">The presented certificate, or <see langword="null"/> when the peer sent none.</param>
    /// <param name="tlsFingerprint">The pinned fingerprint.</param>
    /// <returns><see langword="true"/> when the certificate is the pinned one.</returns>
    /// <remarks>
    /// The answer of a validation callback is a refusal or an acceptance, never an exception: a stored fingerprint that is
    /// not a fingerprint refuses the connection like any other mismatch.
    /// </remarks>
    private static bool IsPinned(X509Certificate? certificate, string tlsFingerprint)
    {
        if (certificate is null)
        {
            return false;
        }

        using var presented = new X509Certificate2(certificate);
        try
        {
            return NodeCertificateStore.Matches(presented, tlsFingerprint);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
