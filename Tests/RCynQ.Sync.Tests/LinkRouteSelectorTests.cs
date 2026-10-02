using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Ids;
using RCynQ.Sync.Routing;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="LinkRouteSelector"/>: which route a link takes and when it moves to another.
/// </summary>
public sealed class LinkRouteSelectorTests
{
    /// <summary>The route through the tunnel over UDP, at the address the peer has first.</summary>
    private static readonly LinkRoute _udp = new(Guid.NewGuid(), "udp", new SshEndpoint("jump", "10.8.0.1", 22));

    /// <summary>The route through the tunnel over TCP.</summary>
    private static readonly LinkRoute _tcp = new(Guid.NewGuid(), "tcp", new SshEndpoint("jump", "10.9.0.1", 22));

    /// <summary>The link repository substitute, backed by <see cref="_stored"/>.</summary>
    private readonly IChainLinkRepository _links = Substitute.For<IChainLinkRepository>();

    /// <summary>The probe substitute.</summary>
    private readonly ILinkRouteProbe _probe = Substitute.For<ILinkRouteProbe>();

    /// <summary>The event publisher substitute.</summary>
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();

    /// <summary>The stored links.</summary>
    private readonly Dictionary<Guid, ChainLinkConfig> _stored = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRouteSelectorTests"/> class.
    /// </summary>
    public LinkRouteSelectorTests()
    {
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(_ => _stored.Values.ToArray());
        _links.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(call => _stored.GetValueOrDefault(call.Arg<Guid>()));
        _links.UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var link = call.Arg<ChainLinkConfig>();
            _stored[link.Id] = link;
            return link;
        });
    }

    /// <summary>
    /// Verifies that a link leaves a route that stopped answering for one that answers.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Moves_To_The_Route_That_Answers()
    {
        var link = Store(RelayLink(_udp, _tcp));
        Answers(_udp, null);
        Answers(_tcp, 40);

        var routes = await Create().CheckAsync();

        _stored[link.Id].Peer.Host.Should().Be("10.9.0.1");
        routes.Should().ContainSingle().Which.Routes.Single(route => route.IsActive).Name.Should().Be("tcp");
        routes[0].Routes.Single(route => route.Name == "udp").Reachability.Should().Be(RouteReachability.Unreachable);
        await _events.Received(1).PublishLinkRoutesAsync(Arg.Is<LinkRoutesChangedEvent>(evt => evt.Switched), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the link returns to the preferred route once it answers again, even when the other one is faster.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Returns_To_The_Preferred_Route_When_It_Answers_Again()
    {
        var preferred = _udp;
        var spare = _tcp.WithPriority(1);
        var link = Store(RelayLink(preferred, spare).WithActiveRoute(spare.Id));
        Answers(preferred, 80);
        Answers(spare, 5);

        await Create().CheckAsync();

        _stored[link.Id].Peer.Host.Should().Be("10.8.0.1");
    }

    /// <summary>
    /// Verifies that among routes of the same preference the fastest one wins, but only when it is clearly faster.
    /// </summary>
    [Fact]
    public void Equal_Routes_Are_Decided_By_Latency_With_A_Margin()
    {
        var link = RelayLink(_udp, _tcp);
        var options = new RouteCheckOptions();

        LinkRouteSelector.Choose(link, Checks((_udp, 100), (_tcp, 20)), options)!.Name.Should().Be("tcp");
        LinkRouteSelector.Choose(link, Checks((_udp, 100), (_tcp, 90)), options)!.Name.Should().Be("udp", "routes of similar speed must not take turns");
        LinkRouteSelector.Choose(link, Checks((_udp, null), (_tcp, null)), options)!.Name.Should().Be("udp", "nothing answers, so moving gains nothing");
    }

    /// <summary>
    /// Verifies that a route pinned by hand is used whether it answers or not, and that the choice can be handed back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pinned_Route_Is_Used_Until_The_Choice_Is_Handed_Back()
    {
        var link = Store(RelayLink(_udp, _tcp));
        Answers(_udp, 10);
        Answers(_tcp, null);
        var selector = Create();

        var pinned = await selector.SelectAsync(link.Id, _tcp.Id);
        await selector.CheckAsync();

        pinned.PinnedRouteId.Should().Be(_tcp.Id);
        _stored[link.Id].Peer.Host.Should().Be("10.9.0.1", "a pinned route is not left although it does not answer");

        await selector.SelectAsync(link.Id, null);
        await selector.CheckAsync();

        _stored[link.Id].PinnedRouteId.Should().BeNull();
        _stored[link.Id].Peer.Host.Should().Be("10.8.0.1");
    }

    /// <summary>
    /// Verifies that a link with a single address is neither checked nor changed: such a node works as it did before
    /// routes existed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Without_Alternative_Routes_Is_Left_Alone()
    {
        var peer = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "10.8.0.1", 22, "jump");
        Store(new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, peer, 0, 22, 10));
        var selector = Create();

        (await selector.CheckAsync()).Should().BeEmpty();
        (await selector.GetRoutesAsync()).Should().BeEmpty();

        await _probe.DidNotReceiveWithAnyArgs().ProbeAsync(default!, default!, default!, default);
        await _links.DidNotReceive().UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a peer behind a Relay is checked through that Relay, at the address the Relay is reached at now,
    /// while a Relay itself checks every neighbour directly.
    /// </summary>
    /// <param name="role">The role of the local node.</param>
    /// <param name="hops">The number of links the check of the endpoint passes.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(NodeRole.Workstation, 1)]
    [InlineData(NodeRole.Relay, 0)]
    public async Task Peer_Behind_A_Relay_Is_Checked_Through_It(NodeRole role, int hops)
    {
        var relay = Store(RelayLink(_udp, _tcp));
        var origin = new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "192.168.1.5", 22, "rcynq");
        var direct = new LinkRoute(Guid.NewGuid(), "lan", origin.Ssh);
        var other = new LinkRoute(Guid.NewGuid(), "vpn", new SshEndpoint("rcynq", "10.7.0.5", 22));
        Store(new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, origin, 0, 18443, 1000, routes: new[] { direct, other }));
        Answers(_udp, null);
        Answers(_tcp, 30);
        Answers(direct, 10);
        Answers(other, 10);

        await Create(role).CheckAsync();

        await _probe.Received().ProbeAsync(
            Arg.Is<ChainLinkConfig>(link => link.Name == "origin"),
            Arg.Any<LinkRoute>(),
            Arg.Is<IReadOnlyList<ChainLinkConfig>>(passed => passed.Count == hops && passed.All(hop => hop.Id == relay.Id && hop.Peer.Host == "10.9.0.1")),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that removing routes moves the link off a removed route and ends with a link that has one address and
    /// no routes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Removing_Routes_Leaves_A_Link_With_One_Address()
    {
        var third = new LinkRoute(Guid.NewGuid(), "lte", new SshEndpoint("jump", "10.10.0.1", 22));
        var link = Store(RelayLink(_udp, _tcp, third));
        var selector = Create();

        var left = await selector.RemoveAsync(link.Id, _udp.Id);

        left.Routes.Should().HaveCount(2);
        _stored[link.Id].ActiveRoute.Should().NotBeNull("the link moved off the removed route");

        await selector.RemoveAsync(link.Id, third.Id);

        _stored[link.Id].Routes.Should().BeEmpty();
        _stored[link.Id].Peer.Host.Should().Be("10.9.0.1");
    }

    /// <summary>
    /// Verifies that the preference of a route can be changed and that an unknown route is reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Priority_Of_A_Route_Can_Be_Changed()
    {
        var link = Store(RelayLink(_udp, _tcp));
        var selector = Create();

        var routes = await selector.SetPriorityAsync(link.Id, _udp.Id, 5);

        routes.Routes.Select(route => route.Name).Should().Equal("tcp", "udp");
        var unknown = () => selector.SetPriorityAsync(link.Id, Guid.NewGuid(), 1);
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>
    /// Creates the selector.
    /// </summary>
    /// <param name="role">The role of the local node.</param>
    /// <returns>The selector.</returns>
    private LinkRouteSelector Create(NodeRole role = NodeRole.Workstation)
        => new(
            new NodeConfig(new NodeId("node-1"), "Node", role, null, role == NodeRole.Workstation ? "/work" : null),
            _links,
            _probe,
            _events,
            TimeProvider.System,
            new RouteCheckOptions());

    /// <summary>
    /// Stores a link.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The link.</returns>
    private ChainLinkConfig Store(ChainLinkConfig link)
    {
        _stored[link.Id] = link;
        return link;
    }

    /// <summary>
    /// Sets the outcome of the check of a route.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="milliseconds">The latency of the answer, or <see langword="null"/> when the route does not answer.</param>
    private void Answers(LinkRoute route, int? milliseconds)
        => _probe.ProbeAsync(Arg.Any<ChainLinkConfig>(), Arg.Is<LinkRoute>(candidate => candidate.Id == route.Id), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(Outcome(milliseconds));

    /// <summary>
    /// Builds the outcomes of a check.
    /// </summary>
    /// <param name="outcomes">The routes with the latency of their answer, or <see langword="null"/> for no answer.</param>
    /// <returns>The outcomes by route identifier.</returns>
    private static Dictionary<Guid, RouteProbeResult> Checks(params (LinkRoute Route, int? Milliseconds)[] outcomes)
        => outcomes.ToDictionary(outcome => outcome.Route.Id, outcome => Outcome(outcome.Milliseconds));

    /// <summary>
    /// Builds the outcome of one check.
    /// </summary>
    /// <param name="milliseconds">The latency, or <see langword="null"/> for no answer.</param>
    /// <returns>The outcome.</returns>
    private static RouteProbeResult Outcome(int? milliseconds)
        => milliseconds is { } value ? new RouteProbeResult(true, TimeSpan.FromMilliseconds(value)) : RouteProbeResult.Unreachable;

    /// <summary>
    /// Builds a link to a Relay with the given routes; the first one is in use.
    /// </summary>
    /// <param name="routes">The routes.</param>
    /// <returns>The link.</returns>
    private static ChainLinkConfig RelayLink(params LinkRoute[] routes)
    {
        var address = routes[0].Ssh;
        var peer = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, address.Host, address.Port, address.User);
        return new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, peer, 0, 22, 10, routes: routes);
    }
}
