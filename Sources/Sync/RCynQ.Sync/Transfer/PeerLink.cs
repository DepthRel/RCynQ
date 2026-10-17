using RCynQ.Core.Api;
using RCynQ.Ssh.Routing;

namespace RCynQ.Sync.Transfer;

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
        var request = CreateRequest(method, path);
        request.Headers.Add(ApiHeaders.NODE_ID, nodeId);
        request.Headers.Add(ApiHeaders.LINK_TOKEN, token);
        return request;
    }

    /// <summary>
    /// Creates a request to the peer without authentication headers: an enrollment, which its ticket protects.
    /// </summary>
    /// <param name="method">The HTTP method.</param>
    /// <param name="path">The path.</param>
    /// <returns>The request.</returns>
    public HttpRequestMessage CreateRequest(HttpMethod method, string path)
    {
        var authority = Host.Contains(':', StringComparison.Ordinal) && !Host.StartsWith('[') ? $"[{Host}]" : Host;
        return new HttpRequestMessage(method, $"{Scheme}://{authority}:{Port}{path}");
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
