using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="PeerAccessPlanner"/> and <see cref="PeerAccessService"/>.
/// </summary>
public sealed class PeerAccessTests : IDisposable
{
    /// <summary>The SSH key of the Workstation peer.</summary>
    private const string WORKSTATION_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:workstation-1";

    /// <summary>The SSH key of the Origin peer.</summary>
    private const string ORIGIN_KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb rcynq:origin-1";

    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-peer-access", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that Origin lets Workstation only reach the local endpoint (and Git where allowed).
    /// </summary>
    [Fact]
    public void Origin_Allows_Workstation_To_Local_Endpoint()
    {
        var origin = new NodeConfig(new NodeId("origin-1"), "Origin", NodeRole.Origin, "/srv/repo", null);
        var peers = new[] { Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY), Peer("relay-1", NodeRole.Relay, "192.168.10.5", null) };

        var plan = PeerAccessPlanner.Plan(origin, 18443, peers);
        plan.Entries.Should().ContainSingle().Which.Line.Should()
            .StartWith("restrict,port-forwarding,permitopen=\"127.0.0.1:18443\",command=\"exit 1\"")
            .And.EndWith("rcynq:workstation-1")
            .And.NotContain("git-upload-pack", "branches travel as bundles through the forwarded port; the key runs no command on the Origin")
            .And.NotContain("/srv/repo", "the path of a repository is never written into the access of a neighbour");
        plan.MissingKeys.Should().BeEmpty("an Origin node does not accept connections from the Relay");
    }

    /// <summary>
    /// Verifies that Workstation lets Origin reach its endpoint and reports a missing key.
    /// </summary>
    [Fact]
    public void Workstation_Allows_Origin_And_Reports_Missing_Key()
    {
        var workstation = new NodeConfig(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, "/work");

        var plan = PeerAccessPlanner.Plan(workstation, 18500, new[] { Peer("origin-1", NodeRole.Origin, "192.168.10.20", ORIGIN_KEY) });
        plan.Entries.Should().ContainSingle().Which.Line.Should().Contain("permitopen=\"127.0.0.1:18500\",command=\"exit 1\"");

        var missing = PeerAccessPlanner.Plan(workstation, 18443, new[] { Peer("origin-1", NodeRole.Origin, "192.168.10.20", null) });
        missing.Entries.Should().BeEmpty();
        missing.MissingKeys.Select(peer => peer.Id.Value).Should().Equal("origin-1");
    }

    /// <summary>
    /// Verifies that a Relay lets each endpoint reach only the opposite endpoint.
    /// </summary>
    [Fact]
    public void Relay_Allows_Each_Endpoint_Only_To_The_Other()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);
        var workstation = Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY);
        var origin = Peer("origin-1", NodeRole.Origin, "192.168.10.20", ORIGIN_KEY);

        var waiting = PeerAccessPlanner.Plan(relay, 18443, new[] { workstation });
        waiting.Entries.Should().BeEmpty();
        waiting.WaitingForCounterpart.Select(peer => peer.Id.Value).Should().Equal("workstation-1");

        var plan = PeerAccessPlanner.Plan(relay, 18443, new[] { workstation, origin });
        plan.Entries.Should().HaveCount(2);
        var workstationLine = plan.Entries.Single(entry => entry.Peer.Id == workstation.Id).Line;
        workstationLine.Should().Contain("permitopen=\"192.168.10.20:22\"").And.NotContain("10.8.1.3");
        var originLine = plan.Entries.Single(entry => entry.Peer.Id == origin.Id).Line;
        originLine.Should().Contain("permitopen=\"10.8.1.3:22\"").And.NotContain("192.168.10.20");
        PeerAccessPlanner.FormatTarget("fd00::5", 22).Should().Be("[fd00::5]:22");
    }

    /// <summary>
    /// Verifies that a Relay lets the endpoints pass on to a neighbouring Relay, which is how a chain crosses two of them.
    /// </summary>
    [Fact]
    public void Relay_Allows_Endpoints_To_Forward_To_A_Neighbouring_Relay()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);
        var workstation = Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY);
        var next = new PeerDescriptor(new NodeId("relay-2"), "relay-2", NodeRole.Relay, "198.51.100.7", 2222, "jump");

        var plan = PeerAccessPlanner.Plan(relay, 18443, new[] { workstation, next });

        plan.WaitingForCounterpart.Should().BeEmpty("the next Relay is a place to forward to");
        plan.Entries.Should().ContainSingle("a Relay logs in nowhere itself, so its own key gets no line")
            .Which.Line.Should().Contain("permitopen=\"198.51.100.7:2222\"");
    }

    /// <summary>
    /// Verifies that a neighbour known at several addresses may be reached at each of them, so the endpoints can change
    /// the route without the Relay.
    /// </summary>
    [Fact]
    public void Relay_Allows_Every_Route_Of_A_Neighbour()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);
        var workstation = Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY);
        var origin = Peer("origin-1", NodeRole.Origin, "192.168.10.20", ORIGIN_KEY);
        var link = new ChainLinkConfig(
            Guid.NewGuid(), "origin", TransportKind.Direct, origin, 0, 22, 10,
            routes: new[]
            {
                new LinkRoute(Guid.NewGuid(), "udp", origin.Ssh),
                new LinkRoute(Guid.NewGuid(), "tcp", new SshEndpoint("user", "10.9.0.20", 2200))
            });

        var plan = PeerAccessPlanner.Plan(relay, 18443, new[] { workstation, origin }, new[] { link });

        plan.Entries.Single(entry => entry.Peer.Id == workstation.Id).Line.Should()
            .Contain("permitopen=\"192.168.10.20:22\"")
            .And.Contain("permitopen=\"10.9.0.20:2200\"");
    }

    /// <summary>
    /// Verifies that a relay lets Workstation reach the TLS port of an Origin that runs no SSH server.
    /// </summary>
    [Fact]
    public void Relay_Forwards_To_The_Tls_Port_Of_A_Origin_Without_Ssh()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);
        var workstation = Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY);
        var origin = new PeerDescriptor(
            new NodeId("origin-1"), "origin-1", NodeRole.Origin, "192.168.10.20", 22, "user", sshPublicKey: ORIGIN_KEY,
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444);

        var plan = PeerAccessPlanner.Plan(relay, 18443, new[] { workstation, origin });

        plan.Entries.Single(entry => entry.Peer.Id == workstation.Id).Line.Should()
            .Contain("permitopen=\"192.168.10.20:18444\"", "the Origin has no SSH server, only its TLS port")
            .And.NotContain("192.168.10.20:22");
    }

    /// <summary>
    /// Verifies that a node without an SSH server does not touch authorized_keys.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Node_Without_Ssh_Server_Needs_No_Authorized_Keys()
    {
        var origin = new NodeConfig(new NodeId("origin-1"), "Origin", NodeRole.Origin, "/srv/repo", null);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY) });
        var options = new SyncBridgeOptions { Endpoint = new EndpointOptions { Mode = EndpointMode.Direct } };
        var adminPath = Path.Combine(_root, "admin", AuthorizedKeysLocation.ADMINISTRATORS_FILE_NAME);
        var service = new PeerAccessService(TestTexts.English, origin, options, peers, new AuthorizedKeysManager(new AuthorizedKeysTarget(adminPath, true), () => false));

        var result = await service.ApplyAsync();

        result.RequiresElevation.Should().BeFalse("no UAC prompt for a file nobody reads");
        result.EntryCount.Should().Be(0);
        result.Message.Should().Contain("not needed");
        File.Exists(adminPath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the service writes the lines and does not touch the administrators file without elevation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Writes_Lines_Or_Requires_Elevation()
    {
        var origin = new NodeConfig(new NodeId("origin-1"), "Origin", NodeRole.Origin, "/srv/repo", null);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Peer("workstation-1", NodeRole.Workstation, "10.8.1.3", WORKSTATION_KEY) });
        var options = new SyncBridgeOptions { ListenPort = 18443 };
        var path = Path.Combine(_root, "authorized_keys");

        var service = new PeerAccessService(TestTexts.English, origin, options, peers, new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path }));
        var result = await service.ApplyAsync();
        result.Applied.Should().BeTrue();
        result.EntryCount.Should().Be(1);
        (await File.ReadAllTextAsync(path)).Should().Contain("rcynq:workstation-1").And.Contain("permitopen=\"127.0.0.1:18443\"");
        (await service.ApplyAsync()).Message.Should().Contain("0 line(s) changed");

        var adminPath = Path.Combine(_root, "admin", AuthorizedKeysLocation.ADMINISTRATORS_FILE_NAME);
        var elevated = new PeerAccessService(TestTexts.English, origin, options, peers, new AuthorizedKeysManager(new AuthorizedKeysTarget(adminPath, true), () => false));
        var denied = await elevated.ApplyAsync();
        denied.RequiresElevation.Should().BeTrue();
        denied.Applied.Should().BeFalse();
        File.Exists(adminPath).Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates a peer.
    /// </summary>
    /// <param name="id">The identifier.</param>
    /// <param name="role">The role.</param>
    /// <param name="host">The host.</param>
    /// <param name="key">The SSH public key line, or <see langword="null"/>.</param>
    /// <returns>The peer.</returns>
    private static PeerDescriptor Peer(string id, NodeRole role, string host, string? key)
        => new(new NodeId(id), id, role, host, 22, "user", sshPublicKey: key);
}
