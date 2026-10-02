using System.Net;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Sync.Reachability;
using RCynQ.Sync.Security;
using RCynQ.Sync.Tests.Fakes;
using RCynQ.Sync.Tests.Fixtures;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the ping that tells the state of the link to the counterpart endpoint the way a patch travels: through
/// the relay, with the link token.
/// </summary>
public sealed class EndpointPingerTests : IDisposable
{
    /// <summary>The shared Workstation and Origin fixture; the node under test is Workstation.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The forward manager substitute: the path through the relay.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The event publisher substitute.</summary>
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();

    /// <summary>The peer that answers the ping.</summary>
    private readonly PingPeer _peer = new();

    /// <summary>The clock.</summary>
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>The contacts the node has had from its peers.</summary>
    private readonly PeerContactRegistry _contacts = new();

    /// <summary>The chain of Workstation: a relay and Origin.</summary>
    private readonly IReadOnlyList<ChainLinkConfig> _chain;

    /// <summary>The factory of the links to the peer.</summary>
    private readonly PeerLinkFactory _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointPingerTests"/> class.
    /// </summary>
    public EndpointPingerTests()
    {
        _chain = _fixture.CreateChain();
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(45200);
        _links = new PeerLinkFactory(_peer, _forwards);
        _peer.Answer = () => PingPeer.Json(HttpStatusCode.OK, $$"""{"nodeId":"{{_fixture.OriginNode.Id}}","role":"Origin","answeredAtUtc":"2026-10-01T12:00:00+00:00"}""");
    }

    /// <summary>Gets the link to the relay.</summary>
    private ChainLinkConfig Hop => _chain[0];

    /// <summary>Gets the link to the counterpart endpoint.</summary>
    private ChainLinkConfig Endpoint => _chain[1];

    /// <summary>
    /// Verifies that only the link to the counterpart endpoint is answered: the link to a relay is left to its
    /// transport, and so is every link of a relay.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_The_Link_To_The_Counterpart_Endpoint_Is_Pinged()
    {
        using var workstation = Pinger();
        using var relay = Pinger(node: new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null));

        (await workstation.GetStateAsync(Hop, _chain)).Should().BeNull();
        (await relay.GetStateAsync(Endpoint, _chain)).Should().BeNull();
        _peer.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies the ping itself: it goes through the path of a patch, carries the identifier of the node and the link
    /// token, and reports the peer as ready with the relay it passed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ping_Goes_The_Way_Of_A_Patch_And_Reports_The_Peer_Ready()
    {
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Ready);
        state.LinkId.Should().Be(Endpoint.Id);
        state.Message.Should().Contain("origin-1").And.Contain("relay-1");
        state.UpdatedAtUtc.Should().Be(_time.GetUtcNow());
        var request = _peer.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.Uri.Should().Be(new Uri("http://127.0.0.1:45200" + ApiRoutes.PEER_PING));
        request.NodeId.Should().Be("workstation-1");
        request.LinkToken.Should().Be(SyncFixture.TOKEN);
        await _forwards.Received(1).StopAsync(45200, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a request for the state does not wait for the ping: it answers "being checked" at once, the ping
    /// runs behind it, and its outcome is published for the application to show.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task State_Is_Answered_At_Once_And_The_Outcome_Is_Published()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var published = new TaskCompletionSource<LinkState>(TaskCreationOptions.RunContinuationsAsynchronously);
        _peer.Gate = release.Task;
        _events.PublishLinkStateAsync(Arg.Any<LinkStateChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => published.TrySetResult(call.Arg<LinkStateChangedEvent>().State));
        using var pinger = Pinger();

        var first = await pinger.GetStateAsync(Endpoint, _chain);
        var again = await pinger.GetStateAsync(Endpoint, _chain);

        first!.Status.Should().Be(LinkStatus.Starting, "the ping has not answered yet");
        again!.Status.Should().Be(LinkStatus.Starting);
        release.SetResult();
        (await published.Task.WaitAsync(TimeSpan.FromSeconds(10))).Status.Should().Be(LinkStatus.Ready);
        (await pinger.GetStateAsync(Endpoint, _chain))!.Status.Should().Be(LinkStatus.Ready);
        _peer.Requests.Should().ContainSingle("a second request does not start a second ping");
    }

    /// <summary>
    /// Verifies that the outcome is kept for a while, and that a request after that time shows the last outcome and
    /// asks again behind it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Outcome_Is_Kept_For_A_While_And_Then_Asked_Again()
    {
        var published = new SemaphoreSlim(0);
        _events.PublishLinkStateAsync(Arg.Any<LinkStateChangedEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(_ => published.Release());
        using var pinger = Pinger();
        await pinger.GetStateAsync(Endpoint, _chain);
        (await published.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();

        _time.Advance(TimeSpan.FromSeconds(29));
        (await pinger.GetStateAsync(Endpoint, _chain))!.Status.Should().Be(LinkStatus.Ready);
        _peer.Requests.Should().ContainSingle("the outcome is still fresh");

        _time.Advance(TimeSpan.FromSeconds(2));
        (await pinger.GetStateAsync(Endpoint, _chain))!.Status.Should().Be(LinkStatus.Ready, "the last outcome is shown while the new one is on its way");
        (await published.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
        _peer.Requests.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies that a peer that answers but refuses this node (a wrong token, a node it does not know) is reported
    /// with its reason, not as a missing connection.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Peer_That_Refuses_The_Node_Is_Reported_With_Its_Reason()
    {
        _peer.Answer = () => PingPeer.Json(HttpStatusCode.Unauthorized, """{"reason":"InvalidToken","error":"The link token is missing or invalid."}""");
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("origin-1").And.Contain("The link token is missing or invalid.");
    }

    /// <summary>
    /// Verifies that a peer of a version that has no ping yet is reported as reachable but not confirmed, instead of
    /// as a failure: the path to it works. Such a peer answers the unknown route the way it answers any route that is
    /// not for neighbours.
    /// </summary>
    /// <param name="status">The status code of the answer.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.NotFound)]
    public async Task Peer_Of_An_Earlier_Version_Is_Reachable_But_Not_Confirmed(HttpStatusCode status)
    {
        _peer.Answer = () => PingPeer.Json(status, """{"error":"The local API token is missing or invalid."}""");
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Degraded);
        state.Message.Should().Contain("origin-1");
    }

    /// <summary>
    /// Verifies that an answer of another node than the one the link names is a failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Answer_Of_Another_Node_Is_A_Failure()
    {
        _peer.Answer = () => PingPeer.Json(HttpStatusCode.OK, """{"nodeId":"somebody-else","role":"Origin","answeredAtUtc":"2026-10-01T12:00:00+00:00"}""");
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("somebody-else").And.Contain("origin-1");
    }

    /// <summary>
    /// Verifies that a path that cannot be opened (the relay does not answer, its host key is not the pinned one) is
    /// reported with what stopped it, and that the peer is not asked at all.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Path_That_Cannot_Be_Opened_Is_Reported_With_Its_Cause()
    {
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SshExecutionException("ssh: connect to host relay.example port 22: Connection timed out"));
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("Connection timed out");
        _peer.Requests.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that an open path with nobody answering at its end (the service of the peer is not running) is told
    /// apart from a path that cannot be opened.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Peer_Service_That_Does_Not_Answer_Is_Reported()
    {
        _peer.Answer = () => throw new HttpRequestException("Connection refused");
        using var pinger = Pinger(wait: true);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("origin-1").And.Contain("Connection refused");
        await _forwards.Received(1).StopAsync(45200, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a node without a link token does not connect at all: there is nothing to be ready with.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Node_Without_A_Link_Token_Does_Not_Connect()
    {
        using var pinger = Pinger(wait: true, token: string.Empty);

        var state = await pinger.GetStateAsync(Endpoint, _chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        await _forwards.DidNotReceive().StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the other half of the exchange: when the peer has reached this node, the state says so.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task State_Tells_When_The_Peer_Reached_This_Node()
    {
        using var pinger = Pinger(wait: true);
        var before = (await pinger.GetStateAsync(Endpoint, _chain))!.Message;
        _contacts.Record(_fixture.OriginNode.Id, _time.GetUtcNow());

        var after = (await pinger.GetStateAsync(Endpoint, _chain))!.Message;

        after.Should().NotBe(before);
        after.Should().StartWith(before);
    }

    /// <summary>
    /// Verifies that the pinger can be disposed more than once: the service container, which offers it under two
    /// contracts, does exactly that when the service stops.
    /// </summary>
    [Fact]
    public void Pinger_Can_Be_Disposed_More_Than_Once()
    {
        var pinger = Pinger();

        pinger.Dispose();
        var again = pinger.Dispose;

        again.Should().NotThrow();
    }

    /// <summary>
    /// Disposes the factory and the fixture.
    /// </summary>
    public void Dispose()
    {
        _links.Dispose();
        _fixture.Dispose();
    }

    /// <summary>
    /// Creates the pinger of a node.
    /// </summary>
    /// <param name="wait">Whether a request for the state waits for the ping, as the command line does.</param>
    /// <param name="node">The node; Workstation when omitted.</param>
    /// <param name="token">The link token of the node.</param>
    /// <returns>The pinger.</returns>
    private EndpointPinger Pinger(bool wait = false, NodeConfig? node = null, string token = SyncFixture.TOKEN)
        => new(
            TestTexts.English,
            node ?? _fixture.WorkstationNode,
            _links,
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = token }, new InMemorySecretStore()),
            _contacts,
            _events,
            _time,
            new EndpointPingOptions { WaitForResult = wait });

    /// <summary>
    /// Stands in for the service of the peer at the end of the path.
    /// </summary>
    private sealed class PingPeer : HttpMessageHandler
    {
        /// <summary>Gets the requests received.</summary>
        public List<(HttpMethod Method, Uri Uri, string? NodeId, string? LinkToken)> Requests { get; } = new();

        /// <summary>Gets or sets what the peer answers.</summary>
        public Func<HttpResponseMessage> Answer { get; set; } = () => new HttpResponseMessage(HttpStatusCode.OK);

        /// <summary>Gets or sets a task the peer waits for before it answers.</summary>
        public Task Gate { get; set; } = Task.CompletedTask;

        /// <summary>
        /// Creates a JSON answer.
        /// </summary>
        /// <param name="status">The status code.</param>
        /// <param name="json">The body.</param>
        /// <returns>The answer.</returns>
        public static HttpResponseMessage Json(HttpStatusCode status, string json)
            => new(status) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json") };

        /// <inheritdoc />
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
            {
                Requests.Add((
                    request.Method,
                    request.RequestUri!,
                    request.Headers.TryGetValues(ApiHeaders.NODE_ID, out var node) ? node.Single() : null,
                    request.Headers.TryGetValues(ApiHeaders.LINK_TOKEN, out var token) ? token.Single() : null));
            }

            await Gate.WaitAsync(cancellationToken);
            return Answer();
        }
    }

    /// <summary>
    /// A clock the test moves.
    /// </summary>
    private sealed class ManualTimeProvider : TimeProvider
    {
        /// <summary>The current time.</summary>
        private DateTimeOffset _now;

        /// <summary>
        /// Initializes a new instance of the <see cref="ManualTimeProvider"/> class.
        /// </summary>
        /// <param name="now">The time the clock starts at.</param>
        public ManualTimeProvider(DateTimeOffset now) => _now = now;

        /// <inheritdoc />
        public override DateTimeOffset GetUtcNow() => _now;

        /// <summary>
        /// Moves the clock forward.
        /// </summary>
        /// <param name="by">The time that passes.</param>
        public void Advance(TimeSpan by) => _now += by;
    }
}
