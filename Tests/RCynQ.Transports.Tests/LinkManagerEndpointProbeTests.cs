using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions.Factory;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Transports.Tests;

/// <summary>
/// Tests of where the link manager takes the state of a link from: the counterpart endpoint behind a mediator is not
/// asked directly, because it cannot be reached directly.
/// </summary>
public sealed class LinkManagerEndpointProbeTests
{
    /// <summary>The link to the mediator, a neighbour.</summary>
    private readonly ChainLinkConfig _hop = new(
        Guid.NewGuid(), "hop", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 22, "jump"), 0, 22, 10);

    /// <summary>The link to the counterpart endpoint behind the mediator.</summary>
    private readonly ChainLinkConfig _endpoint = new(
        Guid.NewGuid(), "endpoint", TransportKind.Direct, new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq"), 0, 18443, 1000);

    /// <summary>The event publisher substitute.</summary>
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();

    /// <summary>The transport of the links: it reaches the mediator and cannot reach the endpoint directly.</summary>
    private readonly ITransportLinkProvider _direct = Substitute.For<ITransportLinkProvider>();

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkManagerEndpointProbeTests"/> class.
    /// </summary>
    public LinkManagerEndpointProbeTests()
    {
        _direct.Kind.Returns(TransportKind.Direct);
        _direct.Create(Arg.Any<ChainLinkConfig>()).Returns(call =>
        {
            var config = call.Arg<ChainLinkConfig>();
            var link = Substitute.For<ITransportLink>();
            link.Kind.Returns(TransportKind.Direct);
            var reachable = config.Id == _hop.Id;
            link.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(
                new LinkState(config.Id, config.Name, TransportKind.Direct, reachable ? LinkStatus.Ready : LinkStatus.Failed, reachable ? "reachable" : "not reachable", DateTimeOffset.UtcNow));
            return link;
        });
    }

    /// <summary>
    /// Verifies the reported bug: the counterpart endpoint that answers through the mediator is reported by what the
    /// probe along the chain found, not as "not reachable" because nobody can connect to it directly. The mediator
    /// keeps being asked through its transport, and the manager publishes no event for the endpoint, whose probe
    /// publishes its own outcomes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Endpoint_Behind_A_Mediator_Is_Reported_By_The_Probe_Along_The_Chain()
    {
        var probe = Substitute.For<IEndpointProbe>();
        probe.GetStateAsync(Arg.Is<ChainLinkConfig>(link => link.Id == _endpoint.Id), Arg.Any<IReadOnlyList<ChainLinkConfig>>(), Arg.Any<CancellationToken>())
            .Returns(new LinkState(_endpoint.Id, _endpoint.Name, TransportKind.Direct, LinkStatus.Ready, "answers through mediator-1", DateTimeOffset.UtcNow));

        var states = await Manager(probe).GetStatusAsync();

        states.Select(state => (state.LinkName, state.Status, state.Message)).Should().Equal(
            ("hop", LinkStatus.Ready, "reachable"),
            ("endpoint", LinkStatus.Ready, "answers through mediator-1"));
        _direct.DidNotReceive().Create(Arg.Is<ChainLinkConfig>(link => link.Id == _endpoint.Id));
        await _events.DidNotReceive().PublishLinkStateAsync(Arg.Is<LinkStateChangedEvent>(evt => evt.State.LinkId == _endpoint.Id), Arg.Any<CancellationToken>());
        await probe.Received(1).GetStateAsync(
            Arg.Is<ChainLinkConfig>(link => link.Id == _endpoint.Id),
            Arg.Is<IReadOnlyList<ChainLinkConfig>>(chain => chain.Count == 2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that without a probe, and for a link the probe leaves to its transport, nothing changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Without_A_Probe_Every_Link_Is_Asked_Through_Its_Transport()
    {
        var silent = Substitute.For<IEndpointProbe>();

        var without = await Manager(null).GetStatusAsync();
        var with = await Manager(silent).GetStatusAsync();

        without.Select(state => state.Status).Should().Equal(LinkStatus.Ready, LinkStatus.Failed);
        with.Select(state => state.Status).Should().Equal(LinkStatus.Ready, LinkStatus.Failed);
    }

    /// <summary>
    /// Creates the link manager of a node with a mediator and an endpoint behind it.
    /// </summary>
    /// <param name="probe">The probe of the counterpart endpoint, or <see langword="null"/>.</param>
    /// <returns>The manager.</returns>
    private LinkManager Manager(IEndpointProbe? probe)
    {
        var repository = Substitute.For<IChainLinkRepository>();
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { _hop, _endpoint });
        return new LinkManager(TestTexts.English, repository, new TransportLinkFactory(TestTexts.English, new[] { _direct }), _events, TimeProvider.System, probe);
    }
}
