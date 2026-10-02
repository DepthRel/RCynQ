using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Sync.Configuration;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of the host key fingerprint handling in <see cref="NodeConfigurationTransfer"/>.
/// </summary>
public sealed class NodeConfigurationTransferTests
{
    /// <summary>A valid SHA-256 fingerprint body (43 Base64 characters).</summary>
    private static readonly string FingerprintBody = Convert.ToBase64String(new byte[32]).TrimEnd('=');

    /// <summary>The peer repository substitute.</summary>
    private readonly IPeerRepository _peers = Substitute.For<IPeerRepository>();

    /// <summary>The link repository substitute.</summary>
    private readonly IChainLinkRepository _links = Substitute.For<IChainLinkRepository>();

    /// <summary>The signature service substitute.</summary>
    private readonly ISignatureService _signatures = Substitute.For<ISignatureService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeConfigurationTransferTests"/> class.
    /// </summary>
    public NodeConfigurationTransferTests()
    {
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<ChainLinkConfig>());
        _links.UpsertAsync(Arg.Any<ChainLinkConfig>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<ChainLinkConfig>());
        _peers.UpsertAsync(Arg.Any<PeerDescriptor>(), Arg.Any<CancellationToken>()).Returns(call => call.Arg<PeerDescriptor>());
    }

    /// <summary>
    /// Verifies that the exported fingerprint is normalized and survives serialization.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Includes_Normalized_Fingerprint()
    {
        var exported = await Create(NodeRole.Mediator).ExportAsync("203.0.113.4", "jump", 22, null, FingerprintBody);

        exported.HostKeyFingerprint.Should().Be("SHA256:" + FingerprintBody);
        NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(exported)).HostKeyFingerprint.Should().Be("SHA256:" + FingerprintBody);
    }

    /// <summary>
    /// Verifies that an invalid fingerprint is refused on export and on parse.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Invalid_Fingerprint_Is_Refused()
    {
        var transfer = Create(NodeRole.Mediator);

        var export = () => transfer.ExportAsync("203.0.113.4", "jump", 22, null, "SHA256:short");
        await export.Should().ThrowAsync<ArgumentException>();

        var valid = await transfer.ExportAsync("203.0.113.4", "jump");
        var parse = () => NodeConfigurationTransfer.Parse(NodeConfigurationTransfer.Serialize(valid with { HostKeyFingerprint = "garbage\"with quotes" }));
        parse.Should().Throw<FormatException>();
    }

    /// <summary>
    /// Verifies that an imported fingerprint is pinned on the peer and its link.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Pins_Host_Key_Fingerprint()
    {
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CurrentFormatVersion,
            new NodeId("mediator-1"),
            "Relay",
            NodeRole.Mediator,
            null,
            "203.0.113.4",
            "jump",
            22,
            null,
            18443,
            null,
            DateTimeOffset.UtcNow,
            FingerprintBody);

        var result = await Create(NodeRole.Source).ImportAsync(imported);

        result.LinkCreated.Should().BeTrue();
        await _peers.Received(1).UpsertAsync(Arg.Is<PeerDescriptor>(peer => peer.HostKeyFingerprint == "SHA256:" + FingerprintBody), Arg.Any<CancellationToken>());
        await _links.Received(1).UpsertAsync(Arg.Is<ChainLinkConfig>(link => link.Peer.HostKeyFingerprint == "SHA256:" + FingerprintBody), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the SSH public key of an imported peer is stored for the neighbour access.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Import_Stores_Ssh_Public_Key()
    {
        const string Key = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:source-1";
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CurrentFormatVersion, new NodeId("source-1"), "Source", NodeRole.Source, null,
            "10.8.1.3", "dev", 22, null, 18443, "  " + Key + "  ", DateTimeOffset.UtcNow);

        await Create(NodeRole.Dest).ImportAsync(imported);

        await _peers.Received(1).UpsertAsync(Arg.Is<PeerDescriptor>(peer => peer.SshPublicKey == Key), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that importing a newer export of the counterpart moves its link to the port the export names: a Dest that
    /// switched from the SSH tunnel to its own TLS port is reached there, not on the old loopback port.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reimport_Of_The_Counterpart_Updates_The_Port_Of_Its_Link()
    {
        var destId = new NodeId("dest-1");
        var stored = new PeerDescriptor(destId, "Dest", NodeRole.Dest, "10.8.0.2", 22, "rcynq");
        var link = new ChainLinkConfig(Guid.NewGuid(), "dest-1", TransportKind.Direct, stored, 0, 18443, NodeConfigurationTransfer.EndpointPriority);
        _peers.GetAsync(destId, Arg.Any<CancellationToken>()).Returns(stored);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var tls = "SHA256:" + new string('A', 64);
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CurrentFormatVersion, destId, "Dest", NodeRole.Dest, null,
            "10.8.0.2", "rcynq", 22, null, 18443, null, DateTimeOffset.UtcNow,
            EndpointMode: EndpointMode.Direct, EndpointPort: 18444, TlsFingerprint: tls);

        var result = await Create(NodeRole.Source).ImportAsync(imported);

        result.LinkCreated.Should().BeFalse();
        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.Id == link.Id && saved.RemotePort == 18444 && saved.Peer.EndpointMode == EndpointMode.Direct),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that importing a mediator again leaves the port of its link alone: an export says nothing about it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reimport_Of_A_Mediator_Keeps_The_Port_Of_Its_Link()
    {
        var mediatorId = new NodeId("mediator-1");
        var stored = new PeerDescriptor(mediatorId, "Relay", NodeRole.Mediator, "203.0.113.4", 22, "jump");
        var link = new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, stored, 0, 2222, 10);
        _peers.GetAsync(mediatorId, Arg.Any<CancellationToken>()).Returns(stored);
        _links.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        var imported = new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CurrentFormatVersion, mediatorId, "Relay", NodeRole.Mediator, null,
            "203.0.113.9", "jump", 22, null, 18443, null, DateTimeOffset.UtcNow);

        await Create(NodeRole.Source).ImportAsync(imported);

        await _links.Received(1).UpsertAsync(
            Arg.Is<ChainLinkConfig>(saved => saved.RemotePort == 2222 && saved.Peer.Host == "203.0.113.9"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that saving a hand-written link keeps what an import stored about its peer: the SSH key, the signing key
    /// and the endpoint mode are not part of such a link and must not be erased by it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Configured_Link_Keeps_The_Imported_Details_Of_Its_Peer()
    {
        const string SshKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:dest-1";
        var destId = new NodeId("dest-1");
        var signingKey = Convert.ToBase64String(new byte[] { 1, 2, 3 });
        var tls = "SHA256:" + new string('B', 64);
        _peers.GetAsync(destId, Arg.Any<CancellationToken>()).Returns(new PeerDescriptor(
            destId, "Dest", NodeRole.Dest, "10.8.0.2", 22, "rcynq", "SHA256:" + FingerprintBody, signingKey, "/srv/repo", SshKey, EndpointMode.Direct, tls, 18444));
        var store = new ConfiguredLinkStore(_links, _peers);

        var saved = await store.SaveAsync(new ChainLinkOptions
        {
            Name = "dest", PeerNodeId = "dest-1", PeerRole = NodeRole.Dest, Host = "10.8.0.7", SshUser = "sync", RemotePort = 18444, Priority = 1000
        });

        saved.Peer.Host.Should().Be("10.8.0.7", "the link states the address");
        saved.Peer.SshUser.Should().Be("sync");
        saved.Peer.SshPublicKey.Should().Be(SshKey);
        saved.Peer.SigningPublicKey.Should().Be(signingKey);
        saved.Peer.HostKeyFingerprint.Should().Be("SHA256:" + FingerprintBody);
        saved.Peer.GitRepoPath.Should().Be("/srv/repo");
        saved.Peer.EndpointMode.Should().Be(EndpointMode.Direct);
        saved.Peer.TlsFingerprint.Should().Be(tls);
        saved.Peer.EndpointPort.Should().Be(18444);
    }

    /// <summary>
    /// Verifies that a value the link states wins over the stored one, and that a new peer is saved as written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_A_Configured_Link_Lets_Its_Own_Values_Win()
    {
        var destId = new NodeId("dest-1");
        _peers.GetAsync(destId, Arg.Any<CancellationToken>()).Returns(new PeerDescriptor(destId, "Dest", NodeRole.Dest, "10.8.0.2", 22, "rcynq", gitRepoPath: "/srv/old"));
        var store = new ConfiguredLinkStore(_links, _peers);

        var known = await store.SaveAsync(new ChainLinkOptions { Name = "dest", PeerNodeId = "dest-1", PeerRole = NodeRole.Dest, Host = "10.8.0.2", SshUser = "rcynq", GitRepoPath = "/srv/new" });
        var fresh = await store.SaveAsync(new ChainLinkOptions { Name = "relay", PeerNodeId = "mediator-1", PeerRole = NodeRole.Mediator, Host = "203.0.113.4", SshUser = "jump", RemotePort = 22 });

        known.Peer.GitRepoPath.Should().Be("/srv/new");
        fresh.Peer.SshPublicKey.Should().BeNull();
        fresh.Peer.EndpointMode.Should().Be(EndpointMode.SshTunnel);
    }

    /// <summary>
    /// Creates the transfer for a local node.
    /// </summary>
    /// <param name="role">The local role.</param>
    /// <returns>The transfer.</returns>
    private NodeConfigurationTransfer Create(NodeRole role)
        => new(
            TestTexts.English,
            new NodeConfig(new NodeId("node-1"), "Node", role, role == NodeRole.Dest ? "/srv/repo" : null, role == NodeRole.Source ? "/work" : null),
            new SyncBridgeOptions { NodeId = "node-1", Role = role },
            _signatures,
            _peers,
            _links,
            TimeProvider.System);
}
