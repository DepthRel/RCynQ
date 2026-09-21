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
    /// Creates the transfer for a local node.
    /// </summary>
    /// <param name="role">The local role.</param>
    /// <returns>The transfer.</returns>
    private NodeConfigurationTransfer Create(NodeRole role)
        => new(
            new NodeConfig(new NodeId("node-1"), "Node", role, role == NodeRole.Dest ? "/srv/repo" : null, role == NodeRole.Source ? "/work" : null),
            new SyncBridgeOptions { NodeId = "node-1", Role = role },
            _signatures,
            _peers,
            _links,
            TimeProvider.System);
}
