using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Service.Tests.Support;
using RCynQ.Sync.Reachability;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;
using RCynQ.Sync.Transfer;

namespace RCynQ.Service.Tests;

/// <summary>
/// End-to-end tests of the ping between the endpoints: the pinger of a Workstation against a real Origin service, over the
/// path a patch takes, with the SSH forward through the relay replaced by a direct connection.
/// </summary>
public sealed class PeerPingEndToEndTests : IAsyncLifetime
{
    /// <summary>The Origin service.</summary>
    private ServiceTestHost _host = null!;

    /// <summary>The forward manager, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The chain of Workstation: a relay, which cannot be reached in the test, and Origin behind it.</summary>
    private IReadOnlyList<ChainLinkConfig> _chain = null!;

    /// <summary>
    /// Starts Origin and registers Workstation as its peer.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        _host = await ServiceTestHost.StartAsync(NodeRole.Origin);
        using var signer = new EcdsaSignatureService(TestTexts.English, new InMemorySecretStore());
        var key = Convert.ToBase64String(await signer.ExportPublicKeyAsync());
        await _host.Services.GetRequiredService<IPeerRepository>()
            .UpsertAsync(new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "workstation.local", 22, "rcynq", signingPublicKey: key));
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_host.BaseAddress.Port);
        _chain = new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.invalid", 22, "jump"), 0, 22, 10),
            new ChainLinkConfig(Guid.NewGuid(), "origin", TransportKind.Direct, new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "origin.invalid", 22, "rcynq"), 0, _host.BaseAddress.Port, 1000)
        };
    }

    /// <summary>
    /// Stops the service.
    /// </summary>
    /// <returns>A task representing the cleanup.</returns>
    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// Verifies the whole exchange: Workstation asks through the chain, Origin answers that it is ready, and Origin remembers
    /// that Workstation reached it. An Origin that nobody can connect to directly is reported as ready.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_Learns_That_Origin_Behind_The_Relay_Is_Ready()
    {
        using var pinger = Pinger();

        var state = await pinger.PingAsync(_chain);

        state!.Status.Should().Be(LinkStatus.Ready, state.Message);
        state.LinkName.Should().Be("origin");
        state.Message.Should().Contain("origin-1").And.Contain("relay-1");
        _host.Services.GetRequiredService<PeerContactRegistry>().LastHeard(new NodeId("workstation-1")).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that a Workstation with another link token learns the reason from Origin instead of a bare "no connection".
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Workstation_With_Another_Token_Learns_Why_It_Is_Refused()
    {
        using var pinger = Pinger("not-the-token-of-origin");

        var state = await pinger.PingAsync(_chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("origin-1").And.Contain("link token");
        _host.Services.GetRequiredService<PeerContactRegistry>().LastHeard(new NodeId("workstation-1")).Should().BeNull();
    }

    /// <summary>
    /// Verifies the route itself: it needs no local API token, because a neighbour has none, but it answers only the
    /// counterpart endpoint with the link token, and with the reason otherwise.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ping_Route_Is_Protected_By_The_Link_Token_Not_By_The_Api_Token()
    {
        var client = _host.CreateClient(withApiToken: false);

        var anonymous = await client.GetAsync(ApiRoutes.PEER_PING);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.PEER_PING);
        request.Headers.Add(ApiHeaders.NODE_ID, "workstation-1");
        request.Headers.Add(ApiHeaders.LINK_TOKEN, ServiceTestHost.LINK_TOKEN);
        var known = await client.SendAsync(request);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.Content.ReadFromJsonAsync<PatchRejectionResponse>())!.Reason.Should().Be(PatchRejectionReason.InvalidToken);
        known.StatusCode.Should().Be(HttpStatusCode.OK);
        var pong = (await known.Content.ReadFromJsonAsync<PeerPingResponse>())!;
        pong.NodeId.Should().Be("origin-1");
        pong.Role.Should().Be(NodeRole.Origin);
    }

    /// <summary>
    /// Verifies what the application of Origin gets for its links: the state of the link to Workstation comes from the ping
    /// along the chain and is "being checked" at once, not "failed" because Workstation cannot be connected to directly.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Status_Of_The_Counterpart_Is_Not_A_Direct_Connection_Attempt()
    {
        var links = _host.Services.GetRequiredService<IChainLinkRepository>();
        await links.UpsertAsync(new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "127.0.0.1", 1, "jump"), 0, 22, 10));
        await links.UpsertAsync(new ChainLinkConfig(Guid.NewGuid(), "workstation", TransportKind.Direct, new PeerDescriptor(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, "127.0.0.1", 1, "rcynq"), 0, 18443, 1000));

        var states = await _host.CreateClient().GetFromJsonAsync<List<Core.State.LinkState>>(ApiRoutes.LINK_STATUS);

        states!.Single(state => state.LinkName == "relay").Status.Should().Be(LinkStatus.Failed, "the neighbour is asked directly, and nothing listens there");
        var counterpart = states!.Single(state => state.LinkName == "workstation");
        counterpart.Status.Should().Be(LinkStatus.Starting);
        counterpart.Message.Should().Contain("workstation-1");
    }

    /// <summary>
    /// Creates the pinger of the simulated Workstation.
    /// </summary>
    /// <param name="token">The link token of Workstation.</param>
    /// <returns>The pinger.</returns>
    private EndpointPinger Pinger(string token = ServiceTestHost.LINK_TOKEN)
        => new(
            TestTexts.English,
            new NodeConfig(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, Path.GetTempPath()),
            new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, _forwards),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = token }, new InMemorySecretStore()),
            new PeerContactRegistry(),
            Substitute.For<IEventPublisher>(),
            TimeProvider.System,
            new EndpointPingOptions());
}
