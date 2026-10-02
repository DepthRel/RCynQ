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
/// End-to-end tests of the ping between the endpoints: the pinger of a Source against a real Dest service, over the
/// path a patch takes, with the SSH forward through the mediator replaced by a direct connection.
/// </summary>
public sealed class PeerPingEndToEndTests : IAsyncLifetime
{
    /// <summary>The Dest service.</summary>
    private ServiceTestHost _host = null!;

    /// <summary>The forward manager, replaced by a direct connection.</summary>
    private readonly ILocalForwardManager _forwards = Substitute.For<ILocalForwardManager>();

    /// <summary>The chain of Source: a mediator, which cannot be reached in the test, and Dest behind it.</summary>
    private IReadOnlyList<ChainLinkConfig> _chain = null!;

    /// <summary>
    /// Starts Dest and registers Source as its peer.
    /// </summary>
    /// <returns>A task representing the setup.</returns>
    public async Task InitializeAsync()
    {
        _host = await ServiceTestHost.StartAsync(NodeRole.Dest);
        using var signer = new EcdsaSignatureService(TestTexts.English, new InMemorySecretStore());
        var key = Convert.ToBase64String(await signer.ExportPublicKeyAsync());
        await _host.Services.GetRequiredService<IPeerRepository>()
            .UpsertAsync(new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "source.local", 22, "rcynq", signingPublicKey: key));
        _forwards.StartAsync(Arg.Any<SshEndpoint>(), Arg.Any<IReadOnlyList<SshEndpoint>>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_host.BaseAddress.Port);
        _chain = new[]
        {
            new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.invalid", 22, "jump"), 0, 22, 10),
            new ChainLinkConfig(Guid.NewGuid(), "dest", TransportKind.Direct, new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "dest.invalid", 22, "rcynq"), 0, _host.BaseAddress.Port, 1000)
        };
    }

    /// <summary>
    /// Stops the service.
    /// </summary>
    /// <returns>A task representing the cleanup.</returns>
    public async Task DisposeAsync() => await _host.DisposeAsync();

    /// <summary>
    /// Verifies the whole exchange: Source asks through the chain, Dest answers that it is ready, and Dest remembers
    /// that Source reached it. A Dest that nobody can connect to directly is reported as ready.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_Learns_That_Dest_Behind_The_Mediator_Is_Ready()
    {
        using var pinger = Pinger();

        var state = await pinger.PingAsync(_chain);

        state!.Status.Should().Be(LinkStatus.Ready, state.Message);
        state.LinkName.Should().Be("dest");
        state.Message.Should().Contain("dest-1").And.Contain("mediator-1");
        _host.Services.GetRequiredService<PeerContactRegistry>().LastHeard(new NodeId("source-1")).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that a Source with another link token learns the reason from Dest instead of a bare "no connection".
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Source_With_Another_Token_Learns_Why_It_Is_Refused()
    {
        using var pinger = Pinger("not-the-token-of-dest");

        var state = await pinger.PingAsync(_chain);

        state!.Status.Should().Be(LinkStatus.Failed);
        state.Message.Should().Contain("dest-1").And.Contain("link token");
        _host.Services.GetRequiredService<PeerContactRegistry>().LastHeard(new NodeId("source-1")).Should().BeNull();
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

        var anonymous = await client.GetAsync(ApiRoutes.PeerPing);
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiRoutes.PeerPing);
        request.Headers.Add(ApiHeaders.NodeId, "source-1");
        request.Headers.Add(ApiHeaders.LinkToken, ServiceTestHost.LinkToken);
        var known = await client.SendAsync(request);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anonymous.Content.ReadFromJsonAsync<PatchRejectionResponse>())!.Reason.Should().Be(PatchRejectionReason.InvalidToken);
        known.StatusCode.Should().Be(HttpStatusCode.OK);
        var pong = (await known.Content.ReadFromJsonAsync<PeerPingResponse>())!;
        pong.NodeId.Should().Be("dest-1");
        pong.Role.Should().Be(NodeRole.Dest);
    }

    /// <summary>
    /// Verifies what the application of Dest gets for its links: the state of the link to Source comes from the ping
    /// along the chain and is "being checked" at once, not "failed" because Source cannot be connected to directly.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Status_Of_The_Counterpart_Is_Not_A_Direct_Connection_Attempt()
    {
        var links = _host.Services.GetRequiredService<IChainLinkRepository>();
        await links.UpsertAsync(new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "127.0.0.1", 1, "jump"), 0, 22, 10));
        await links.UpsertAsync(new ChainLinkConfig(Guid.NewGuid(), "source", TransportKind.Direct, new PeerDescriptor(new NodeId("source-1"), "Source", NodeRole.Source, "127.0.0.1", 1, "rcynq"), 0, 18443, 1000));

        var states = await _host.CreateClient().GetFromJsonAsync<List<Core.State.LinkState>>(ApiRoutes.LinkStatus);

        states!.Single(state => state.LinkName == "relay").Status.Should().Be(LinkStatus.Failed, "the neighbour is asked directly, and nothing listens there");
        var counterpart = states!.Single(state => state.LinkName == "source");
        counterpart.Status.Should().Be(LinkStatus.Starting);
        counterpart.Message.Should().Contain("source-1");
    }

    /// <summary>
    /// Creates the pinger of the simulated Source.
    /// </summary>
    /// <param name="token">The link token of Source.</param>
    /// <returns>The pinger.</returns>
    private EndpointPinger Pinger(string token = ServiceTestHost.LinkToken)
        => new(
            TestTexts.English,
            new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, Path.GetTempPath()),
            new PeerLinkFactory(new SocketsHttpHandler { UseProxy = false }, _forwards),
            new LinkTokenProvider(new SyncBridgeOptions { LinkToken = token }, new InMemorySecretStore()),
            new PeerContactRegistry(),
            Substitute.For<IEventPublisher>(),
            TimeProvider.System,
            new EndpointPingOptions());
}
