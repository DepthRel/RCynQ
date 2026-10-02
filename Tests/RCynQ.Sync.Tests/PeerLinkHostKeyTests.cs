using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the order in which a link to a peer is opened: the hosts on the way are made known by their pinned host
/// keys first, and only then the SSH forward is started.
/// </summary>
public sealed class PeerLinkHostKeyTests : IDisposable
{
    /// <summary>The local port the forward substitute hands out.</summary>
    private const int FORWARD_PORT = 45123;

    /// <summary>The forward manager substitute.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The host key guard substitute.</summary>
    private readonly IPinnedHostKeyGuard _hostKeys = Substitute.For<IPinnedHostKeyGuard>();

    /// <summary>The factory under test.</summary>
    private readonly PeerLinkFactory _factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerLinkHostKeyTests"/> class.
    /// </summary>
    public PeerLinkHostKeyTests()
    {
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(FORWARD_PORT);
        _factory = new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, _forwards, hostKeys: _hostKeys);
    }

    /// <summary>
    /// Verifies that the hosts of the route are checked, in the order the SSH session passes them, before the forward
    /// is started.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Host_Keys_Are_Checked_Before_The_Forward_Is_Started()
    {
        await using var link = await _factory.OpenAsync(Route());

        link.Port.Should().Be(FORWARD_PORT);
        Received.InOrder(() =>
        {
            _hostKeys.EnsurePinnedAsync(Arg.Is<IReadOnlyList<PeerDescriptor>>(hops => hops.Select(hop => hop.Id.Value).SequenceEqual(new[] { "relay-1", "origin-1" })), Arg.Any<CancellationToken>());
            _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
        });
    }

    /// <summary>
    /// Verifies that a host that does not own its pinned key gets no connection at all: no forward is started, so
    /// neither the link token nor a patch can travel.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task No_Forward_Is_Started_When_A_Host_Does_Not_Own_Its_Pinned_Key()
    {
        _hostKeys.EnsurePinnedAsync(Arg.Any<IReadOnlyList<PeerDescriptor>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SshExecutionException("not the pinned key"));

        var open = () => _factory.OpenAsync(Route());

        await open.Should().ThrowAsync<SshExecutionException>().WithMessage("not the pinned key");
        await _forwards.DidNotReceive().StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a peer reached on its own TLS port through a relay has only the relay checked: the SSH
    /// session ends there, and the peer itself is held to its pinned certificate instead.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_The_Hosts_Of_The_Ssh_Session_Are_Checked()
    {
        var relay = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.example", 22, "jump", "SHA256:" + new string('B', 43));
        var origin = new PeerDescriptor(
            new NodeId("origin-1"), "Origin", NodeRole.Origin, "192.168.10.20", 22, "rcynq",
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444);
        var route = ChainRoute.Resolve(new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, relay, 0, 22, 10),
            new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, origin, 0, 18444, 100)
        });

        await using var link = await _factory.OpenAsync(route);

        await _hostKeys.Received(1).EnsurePinnedAsync(
            Arg.Is<IReadOnlyList<PeerDescriptor>>(hops => hops.Select(hop => hop.Id.Value).SequenceEqual(new[] { "relay-1" })),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a factory without a guard connects as before.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Factory_Without_A_Guard_Connects()
    {
        using var unguarded = new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, _forwards);

        await using var link = await unguarded.OpenAsync(Route());

        link.Port.Should().Be(FORWARD_PORT);
    }

    /// <summary>
    /// Disposes the factory.
    /// </summary>
    public void Dispose() => _factory.Dispose();

    /// <summary>
    /// Creates the route to an Origin that runs an SSH server, through a relay.
    /// </summary>
    /// <returns>The route.</returns>
    private static ChainRoute Route()
    {
        var relay = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.example", 22, "jump", "SHA256:" + new string('B', 43));
        var origin = new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "origin.internal", 22, "rcynq", "SHA256:" + new string('C', 43));
        return ChainRoute.Resolve(new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, relay, 0, 22, 10),
            new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, origin, 0, 18443, 100)
        });
    }
}
