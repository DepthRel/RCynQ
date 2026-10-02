using System.Net.Security;
using System.Net.Sockets;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Ssh.Keys.HostKeys;

namespace RCynQ.Sync.Routing;

/// <summary>
/// Default <see cref="ILinkRouteProbe"/>: connects to the address of the route the way the traffic would.
/// </summary>
/// <remarks>
/// <para>
/// A peer this node connects to itself is checked with a plain TCP connection to the port it listens on.
/// </para>
/// <para>
/// A peer behind relays cannot be reached from here. It is checked through an SSH forward that passes the relays and
/// ends at the address of the route. Such a forward opens whether or not anybody listens at its far end, so the check
/// waits for a sign of life: the banner of an SSH server, or a completed TLS handshake of a peer that runs none. The
/// certificate is not examined: the check sends nothing and learns only that the peer answers.
/// </para>
/// </remarks>
public sealed class LinkRouteProbe : ILinkRouteProbe
{
    /// <summary>Connects to peers this node reaches itself.</summary>
    private readonly ITcpProbe _tcp;

    /// <summary>Opens the forwards to peers behind relays.</summary>
    private readonly ILocalForwardManager _forwards;

    /// <summary>The clock that measures the latency.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>How the routes are checked.</summary>
    private readonly RouteCheckOptions _options;

    /// <summary>Holds SSH connections to the pinned host keys of the relays, or <see langword="null"/>.</summary>
    private readonly IPinnedHostKeyGuard? _hostKeys;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRouteProbe"/> class.
    /// </summary>
    /// <param name="tcp">Connects to peers this node reaches itself.</param>
    /// <param name="forwards">Opens the forwards to peers behind relays.</param>
    /// <param name="timeProvider">The clock that measures the latency.</param>
    /// <param name="options">How the routes are checked.</param>
    /// <param name="hostKeys">
    /// Holds SSH connections to the pinned host keys of the relays, or <see langword="null"/> to check nothing.
    /// </param>
    public LinkRouteProbe(ITcpProbe tcp, ILocalForwardManager forwards, TimeProvider timeProvider, RouteCheckOptions options, IPinnedHostKeyGuard? hostKeys = null)
    {
        _tcp = tcp ?? throw new ArgumentNullException(nameof(tcp));
        _forwards = forwards ?? throw new ArgumentNullException(nameof(forwards));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _hostKeys = hostKeys;
    }

    /// <inheritdoc />
    public async Task<RouteProbeResult> ProbeAsync(ChainLinkConfig link, LinkRoute route, IReadOnlyList<ChainLinkConfig> hops, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(hops);

        var usesTls = link.Peer.EndpointMode == EndpointMode.Direct;
        var port = usesTls ? link.Peer.EndpointPort : route.Ssh.Port;
        var started = _timeProvider.GetTimestamp();
        bool answered;
        if (hops.Count == 0)
        {
            answered = await _tcp.CanConnectAsync(route.Ssh.Host, port, _options.Timeout, cancellationToken);
        }
        else if (hops.Any(hop => hop.Peer.EndpointMode == EndpointMode.Direct))
        {
            // A forward needs an SSH server at every hop; a chain without one carries no traffic either.
            return RouteProbeResult.Unreachable;
        }
        else
        {
            answered = await AnswersBehindAsync(route.Ssh.Host, port, usesTls, hops, cancellationToken);
        }

        return answered ? new RouteProbeResult(true, _timeProvider.GetElapsedTime(started)) : RouteProbeResult.Unreachable;
    }

    /// <summary>
    /// Checks an address behind relays through a forward that ends at it.
    /// </summary>
    /// <param name="host">The host of the route, as the last relay sees it.</param>
    /// <param name="port">The port the peer listens on.</param>
    /// <param name="usesTls">Whether the peer answers with TLS instead of an SSH banner.</param>
    /// <param name="hops">The relays on the way, in order.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the peer gave a sign of life.</returns>
    private async Task<bool> AnswersBehindAsync(string host, int port, bool usesTls, IReadOnlyList<ChainLinkConfig> hops, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        int localPort;
        try
        {
            if (_hostKeys is not null)
            {
                await _hostKeys.EnsurePinnedAsync(hops.Select(hop => hop.Peer).ToArray(), timeout.Token);
            }

            localPort = await _forwards.StartAsync(
                hops[^1].Peer.Ssh,
                hops.Take(hops.Count - 1).Select(hop => hop.Peer.Ssh).ToArray(),
                0,
                host,
                port,
                timeout.Token);
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        try
        {
            return await ShowsLifeAsync(localPort, host, usesTls, timeout.Token);
        }
        catch (Exception ex) when (ex is SocketException or IOException or System.Security.Authentication.AuthenticationException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            await _forwards.StopAsync(localPort, CancellationToken.None);
        }
    }

    /// <summary>
    /// Waits for the far end of a forward to answer.
    /// </summary>
    /// <param name="localPort">The local port of the forward.</param>
    /// <param name="host">The host at the far end, named in the TLS handshake.</param>
    /// <param name="usesTls">Whether the far end answers with TLS instead of an SSH banner.</param>
    /// <param name="cancellationToken">A token that ends the wait.</param>
    /// <returns><see langword="true"/> when the far end answered.</returns>
    private static async Task<bool> ShowsLifeAsync(int localPort, string host, bool usesTls, CancellationToken cancellationToken)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(System.Net.IPAddress.Loopback, localPort, cancellationToken);
        var stream = client.GetStream();
        if (usesTls)
        {
            // Only the fact of an answer matters here; the certificate is checked where data is sent.
            await using var tls = new SslStream(stream, leaveInnerStreamOpen: true, (_, _, _, _) => true);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, cancellationToken);
            return true;
        }

        // An SSH server speaks first. A forward whose far end nobody listens at is closed without a byte.
        var banner = new byte[4];
        var read = 0;
        while (read < banner.Length)
        {
            var count = await stream.ReadAsync(banner.AsMemory(read), cancellationToken);
            if (count == 0)
            {
                return false;
            }

            read += count;
        }

        return banner.AsSpan().SequenceEqual("SSH-"u8);
    }
}
