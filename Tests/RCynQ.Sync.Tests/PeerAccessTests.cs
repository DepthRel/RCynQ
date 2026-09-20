using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Sync.Configuration.Access;
using Xunit;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="PeerAccessPlanner"/> and <see cref="PeerAccessService"/>.
/// </summary>
public sealed class PeerAccessTests : IDisposable
{
    /// <summary>The SSH key of the Source peer.</summary>
    private const string SourceKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:source-1";

    /// <summary>The SSH key of the Dest peer.</summary>
    private const string DestKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIPBzgtLuBSnvmhyPyE/aR3fT7G5Ynge9cP0KZC9vUVUb rcynq:dest-1";

    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-peer-access", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Verifies that Dest lets Source only reach the local endpoint (and Git where allowed).
    /// </summary>
    [Fact]
    public void Dest_Allows_Source_To_Local_Endpoint()
    {
        var dest = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/repo", null);
        var peers = new[] { Peer("source-1", NodeRole.Source, "10.8.1.3", SourceKey), Peer("mediator-1", NodeRole.Mediator, "192.168.10.5", null) };

        var withGit = PeerAccessPlanner.Plan(dest, 18443, peers, allowGitCommand: true);
        withGit.Entries.Should().ContainSingle().Which.Line.Should()
            .StartWith("restrict,port-forwarding,permitopen=\"127.0.0.1:18443\",command=\"git-upload-pack '/srv/repo'\"")
            .And.EndWith("rcynq:source-1");
        withGit.MissingKeys.Should().BeEmpty("a Dest node does not accept connections from the Mediator");

        PeerAccessPlanner.Plan(dest, 18443, peers, allowGitCommand: false).Entries.Single().Line.Should().Contain("command=\"exit 1\"");

        var spaced = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/my repo", null);
        PeerAccessPlanner.Plan(spaced, 18443, peers, allowGitCommand: true).Entries.Single().Line.Should().Contain("command=\"exit 1\"", "an unquotable repository path falls back to forward-only access");
    }

    /// <summary>
    /// Verifies that Source lets Dest reach its endpoint and reports a missing key.
    /// </summary>
    [Fact]
    public void Source_Allows_Dest_And_Reports_Missing_Key()
    {
        var source = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, "/work");

        var plan = PeerAccessPlanner.Plan(source, 18500, new[] { Peer("dest-1", NodeRole.Dest, "192.168.10.20", DestKey) }, allowGitCommand: true);
        plan.Entries.Should().ContainSingle().Which.Line.Should().Contain("permitopen=\"127.0.0.1:18500\",command=\"exit 1\"");

        var missing = PeerAccessPlanner.Plan(source, 18443, new[] { Peer("dest-1", NodeRole.Dest, "192.168.10.20", null) }, allowGitCommand: true);
        missing.Entries.Should().BeEmpty();
        missing.MissingKeys.Select(peer => peer.Id.Value).Should().Equal("dest-1");
    }

    /// <summary>
    /// Verifies that a Mediator lets each endpoint reach only the opposite endpoint.
    /// </summary>
    [Fact]
    public void Mediator_Allows_Each_Endpoint_Only_To_The_Other()
    {
        var mediator = new NodeConfig(new NodeId("mediator-1"), "Mediator", NodeRole.Mediator, null, null);
        var source = Peer("source-1", NodeRole.Source, "10.8.1.3", SourceKey);
        var dest = Peer("dest-1", NodeRole.Dest, "192.168.10.20", DestKey);

        var waiting = PeerAccessPlanner.Plan(mediator, 18443, new[] { source }, allowGitCommand: true);
        waiting.Entries.Should().BeEmpty();
        waiting.WaitingForCounterpart.Select(peer => peer.Id.Value).Should().Equal("source-1");

        var plan = PeerAccessPlanner.Plan(mediator, 18443, new[] { source, dest }, allowGitCommand: true);
        plan.Entries.Should().HaveCount(2);
        var sourceLine = plan.Entries.Single(entry => entry.Peer.Id == source.Id).Line;
        sourceLine.Should().Contain("permitopen=\"192.168.10.20:22\"").And.NotContain("10.8.1.3");
        var destLine = plan.Entries.Single(entry => entry.Peer.Id == dest.Id).Line;
        destLine.Should().Contain("permitopen=\"10.8.1.3:22\"").And.NotContain("192.168.10.20");
        PeerAccessPlanner.FormatTarget("fd00::5", 22).Should().Be("[fd00::5]:22");
    }

    /// <summary>
    /// Verifies that a mediator lets Source reach the TLS port of a Dest that runs no SSH server.
    /// </summary>
    [Fact]
    public void Mediator_Forwards_To_The_Tls_Port_Of_A_Dest_Without_Ssh()
    {
        var mediator = new NodeConfig(new NodeId("mediator-1"), "Mediator", NodeRole.Mediator, null, null);
        var source = Peer("source-1", NodeRole.Source, "10.8.1.3", SourceKey);
        var dest = new PeerDescriptor(
            new NodeId("dest-1"), "dest-1", NodeRole.Dest, "192.168.10.20", 22, "user", sshPublicKey: DestKey,
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('A', 64), endpointPort: 18444);

        var plan = PeerAccessPlanner.Plan(mediator, 18443, new[] { source, dest }, allowGitCommand: true);

        plan.Entries.Single(entry => entry.Peer.Id == source.Id).Line.Should()
            .Contain("permitopen=\"192.168.10.20:18444\"", "the Dest has no SSH server, only its TLS port")
            .And.NotContain("192.168.10.20:22");
    }

    /// <summary>
    /// Verifies that a node without an SSH server does not touch authorized_keys.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Node_Without_Ssh_Server_Needs_No_Authorized_Keys()
    {
        var dest = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/repo", null);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Peer("source-1", NodeRole.Source, "10.8.1.3", SourceKey) });
        var options = new SyncBridgeOptions { Endpoint = new EndpointOptions { Mode = EndpointMode.Direct } };
        var adminPath = Path.Combine(_root, "admin", AuthorizedKeysLocation.AdministratorsFileName);
        var service = new PeerAccessService(dest, options, peers, new AuthorizedKeysManager(new AuthorizedKeysTarget(adminPath, true), () => false));

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
        var dest = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/srv/repo", null);
        var peers = Substitute.For<IPeerRepository>();
        peers.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { Peer("source-1", NodeRole.Source, "10.8.1.3", SourceKey) });
        var options = new SyncBridgeOptions { ListenPort = 18443 };
        var path = Path.Combine(_root, "authorized_keys");

        var service = new PeerAccessService(dest, options, peers, new AuthorizedKeysManager(new SshClientOptions { AuthorizedKeysPath = path }));
        var result = await service.ApplyAsync();
        result.Applied.Should().BeTrue();
        result.EntryCount.Should().Be(1);
        (await File.ReadAllTextAsync(path)).Should().Contain("rcynq:source-1").And.Contain("permitopen=\"127.0.0.1:18443\"");
        (await service.ApplyAsync()).Message.Should().Contain("0 line(s) changed");

        var adminPath = Path.Combine(_root, "admin", AuthorizedKeysLocation.AdministratorsFileName);
        var elevated = new PeerAccessService(dest, options, peers, new AuthorizedKeysManager(new AuthorizedKeysTarget(adminPath, true), () => false));
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
