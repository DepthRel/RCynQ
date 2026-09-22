using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
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
    /// Initializes a new instance of the <see cref="PeerLinkFactory"/> class.
    /// </summary>
    /// <param name="handler">The HTTP handler for loopback endpoints; production code uses a handler with the system proxy disabled.</param>
    /// <param name="forwards">The forward manager.</param>
    /// <param name="pinnedHandlers">
    /// Creates the handler used for a peer that accepts connections on its own TLS port, given its pinned fingerprint;
    /// <see langword="null"/> uses a handler that trusts that certificate and no other.
    /// </param>
    public PeerLinkFactory(HttpMessageHandler handler, ILocalForwardManager forwards, Func<string, HttpMessageHandler>? pinnedHandlers = null)
    {
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
    public async Task<PeerLink> OpenAsync(ChainRoute route, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        var port = route.UsesSsh
            ? await _forwards.StartAsync(route.Target!, route.Jumps, route.Endpoint.LocalPort, route.RemoteHost, route.RemotePort, cancellationToken)
            : route.RemotePort;
        return new PeerLink(
            route,
            Client(route.TlsFingerprint),
            route.UsesTls ? "https" : "http",
            route.UsesSsh ? "127.0.0.1" : route.RemoteHost,
            port,
            route.UsesSsh ? () => _forwards.StopAsync(port, CancellationToken.None) : null);
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
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate is not null
                    && NodeCertificateStore.Matches(new X509Certificate2(certificate), tlsFingerprint)
            }
        };
}

/// <summary>
/// An open connection to the counterpart endpoint. Disposing it stops the SSH forward it may use.
/// </summary>
public sealed class PeerLink : IAsyncDisposable
{
    /// <summary>
    /// Stops the forward, or <see langword="null"/> when none was started.
    /// </summary>
    private readonly Func<Task>? _stop;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerLink"/> class.
    /// </summary>
    /// <param name="route">The route of the link.</param>
    /// <param name="client">The HTTP client.</param>
    /// <param name="scheme">The URL scheme.</param>
    /// <param name="host">The host.</param>
    /// <param name="port">The port.</param>
    /// <param name="stop">Stops the forward, or <see langword="null"/> when none was started.</param>
    internal PeerLink(ChainRoute route, HttpClient client, string scheme, string host, int port, Func<Task>? stop)
    {
        Route = route;
        Client = client;
        Scheme = scheme;
        Host = host;
        Port = port;
        _stop = stop;
    }

    /// <summary>
    /// Gets the route of the link.
    /// </summary>
    public ChainRoute Route { get; }

    /// <summary>
    /// Gets the HTTP client. It has no timeout of its own; callers bound every request themselves.
    /// </summary>
    public HttpClient Client { get; }

    /// <summary>
    /// Gets the URL scheme.
    /// </summary>
    public string Scheme { get; }

    /// <summary>
    /// Gets the host to connect to.
    /// </summary>
    public string Host { get; }

    /// <summary>
    /// Gets the port to connect to.
    /// </summary>
    public int Port { get; }

    /// <summary>
    /// Creates a request to the peer with the authentication headers.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <param name="nodeId">The identifier of the sending node.</param>
    /// <param name="token">The link token.</param>
    /// <returns>The request.</returns>
    public HttpRequestMessage CreateRequest(HttpMethod method, string path, string nodeId, string token)
    {
        var authority = Host.Contains(':', StringComparison.Ordinal) && !Host.StartsWith('[') ? $"[{Host}]" : Host;
        var request = new HttpRequestMessage(method, $"{Scheme}://{authority}:{Port}{path}");
        request.Headers.Add(ApiHeaders.NodeId, nodeId);
        request.Headers.Add(ApiHeaders.LinkToken, token);
        return request;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_stop is not null)
        {
            await _stop();
        }
    }
}
