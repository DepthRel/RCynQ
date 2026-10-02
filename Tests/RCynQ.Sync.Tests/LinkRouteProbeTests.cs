using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Sync.Routing;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="LinkRouteProbe"/>: how a route is checked. No SSH is run; the forward is a local listener.
/// </summary>
public sealed class LinkRouteProbeTests
{
    /// <summary>The TCP probe substitute.</summary>
    private readonly ITcpProbe _tcp = Substitute.For<ITcpProbe>();

    /// <summary>The forward manager substitute.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The route that is checked.</summary>
    private readonly LinkRoute _route = new(Guid.NewGuid(), "tcp", new SshEndpoint("rcynq", "10.9.0.5", 2200));

    /// <summary>
    /// Verifies that a peer this node connects to itself is checked with a connection to the SSH port of the route.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Neighbour_Is_Checked_With_A_Connection_To_The_Route()
    {
        _tcp.CanConnectAsync("10.9.0.5", 2200, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        var answered = await Create().ProbeAsync(Link(EndpointMode.SshTunnel), _route, Array.Empty<ChainLinkConfig>());
        _tcp.CanConnectAsync("10.9.0.5", 2200, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        var silent = await Create().ProbeAsync(Link(EndpointMode.SshTunnel), _route, Array.Empty<ChainLinkConfig>());

        answered.Reachable.Should().BeTrue();
        answered.Latency.Should().NotBeNull();
        silent.Should().Be(RouteProbeResult.Unreachable);
        await _forwards.DidNotReceiveWithAnyArgs().StartAsync(default!, default!, default, default!, default, default);
    }

    /// <summary>
    /// Verifies that a peer without an SSH server is checked on its TLS port, at the host of the route.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Peer_Without_Ssh_Is_Checked_On_Its_Tls_Port()
    {
        _tcp.CanConnectAsync("10.9.0.5", 18444, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        var result = await Create().ProbeAsync(Link(EndpointMode.Direct), _route, Array.Empty<ChainLinkConfig>());

        result.Reachable.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a peer behind a Relay is checked through a forward that ends at the route, and that the forward
    /// is stopped afterwards. An open forward is not an answer: only the banner of the SSH server at its far end is.
    /// </summary>
    /// <param name="banner">What the far end of the forward says, or an empty text when it closes the connection.</param>
    /// <param name="reachable">Whether the route counts as answering.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("SSH-2.0-OpenSSH_9.6\r\n", true)]
    [InlineData("", false)]
    public async Task Peer_Behind_A_Relay_Is_Checked_Through_A_Forward(string banner, bool reachable)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var farEnd = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(banner));
        });
        var relay = RelayHop();
        _forwards.StartAsync(relay.Peer.Ssh, Arg.Any<IReadOnlyList<SshEndpoint>>(), 0, "10.9.0.5", 2200, Arg.Any<CancellationToken>()).Returns(port);

        var result = await Create().ProbeAsync(Link(EndpointMode.SshTunnel), _route, new[] { relay });
        await farEnd;

        result.Reachable.Should().Be(reachable);
        await _forwards.Received(1).StopAsync(port, Arg.Any<CancellationToken>());
        await _tcp.DidNotReceiveWithAnyArgs().CanConnectAsync(default!, default, default, default);
    }

    /// <summary>
    /// Verifies that a forward that cannot be opened means a route that does not answer, not a failure of the check.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Forward_That_Cannot_Be_Opened_Means_No_Answer()
    {
        _forwards.StartAsync(default!, default!, default, default!, default, default)
            .ReturnsForAnyArgs<int>(_ => throw new SshExecutionException("administratively prohibited"));

        var result = await Create().ProbeAsync(Link(EndpointMode.SshTunnel), _route, new[] { RelayHop() });

        result.Should().Be(RouteProbeResult.Unreachable);
    }

    /// <summary>
    /// Creates the probe.
    /// </summary>
    /// <returns>The probe.</returns>
    private LinkRouteProbe Create()
        => new(_tcp, _forwards, TimeProvider.System, new RouteCheckOptions { Timeout = TimeSpan.FromSeconds(5) });

    /// <summary>
    /// Builds the link whose route is checked.
    /// </summary>
    /// <param name="mode">How the peer accepts connections.</param>
    /// <returns>The link.</returns>
    private static ChainLinkConfig Link(EndpointMode mode)
    {
        var peer = mode == EndpointMode.Direct
            ? new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "192.168.1.5", 22, "rcynq", endpointMode: mode, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444)
            : new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "192.168.1.5", 22, "rcynq");
        return new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, peer, 0, 18443, 1000);
    }

    /// <summary>
    /// Builds the link to the Relay the check passes.
    /// </summary>
    /// <returns>The link.</returns>
    private static ChainLinkConfig RelayHop()
        => new(
            Guid.NewGuid(), "relay", TransportKind.Direct,
            new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "203.0.113.4", 22, "jump"),
            0, 22, 10);
}
