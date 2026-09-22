using System.Text.Json;
using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Core.Tests;

/// <summary>
/// Tests for identifiers, configuration records, patch records and JSON contracts.
/// </summary>
public class CoreRecordTests
{
    /// <summary>
    /// A valid SHA-256 digest used by the tests.
    /// </summary>
    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    /// <summary>
    /// Verifies that node identifiers are trimmed and compared by value.
    /// </summary>
    [Fact]
    public void NodeId_Trims_And_Compares_By_Value()
    {
        new NodeId("  dest-1 ").Should().Be(new NodeId("dest-1"));
        new NodeId("dest-1").ToString().Should().Be("dest-1");
        default(NodeId).IsEmpty.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that malformed node identifiers are rejected.
    /// </summary>
    /// <param name="value">The invalid value.</param>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("has space")]
    [InlineData("semi;colon")]
    [InlineData("new\nline")]
    public void NodeId_Rejects_Invalid_Values(string value)
    {
        var create = () => new NodeId(value);
        create.Should().Throw<ArgumentException>();
        NodeId.TryParse(value, out _).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a node identifier longer than the maximum is rejected.
    /// </summary>
    [Fact]
    public void NodeId_Rejects_Too_Long_Value()
    {
        var create = () => new NodeId(new string('a', NodeId.MaxLength + 1));
        create.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that SSH endpoints refuse option injection and format jump specifications.
    /// </summary>
    [Fact]
    public void SshEndpoint_Validates_And_Formats()
    {
        new SshEndpoint("git", "dest.example", 2222).JumpSpec.Should().Be("git@dest.example:2222");
        new SshEndpoint("git", "[::1]").JumpSpec.Should().Be("git@[::1]:22");
        new SshEndpoint("git", "10.0.0.5").Destination.Should().Be("git@10.0.0.5");

        var injectedHost = () => new SshEndpoint("git", "-oProxyCommand=calc");
        var injectedUser = () => new SshEndpoint("a b", "host");
        var shellMeta = () => new SshEndpoint("git", "host;rm");
        injectedHost.Should().Throw<ArgumentException>();
        injectedUser.Should().Throw<ArgumentException>();
        shellMeta.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies the role-dependent path rules of <see cref="NodeConfig"/>.
    /// </summary>
    [Fact]
    public void NodeConfig_Enforces_Role_Paths()
    {
        var id = new NodeId("n1");
        new NodeConfig(id, "Dest", NodeRole.Dest, "/repo", null).EffectiveWorkdir.Should().Be("/repo");
        new NodeConfig(id, "Mediator", NodeRole.Mediator, null, null).ParticipatesInPatchChannel.Should().BeFalse();

        var sourceWithoutWorkdir = () => new NodeConfig(id, "Source", NodeRole.Source, null, null);
        var destWithoutRepo = () => new NodeConfig(id, "Dest", NodeRole.Dest, null, "/work");
        var mediatorWithRepo = () => new NodeConfig(id, "Mediator", NodeRole.Mediator, "/repo", null);
        sourceWithoutWorkdir.Should().Throw<ArgumentException>();
        destWithoutRepo.Should().Throw<ArgumentException>();
        mediatorWithRepo.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies validation of chain links and removal of the primary transport from fallbacks.
    /// </summary>
    [Fact]
    public void ChainLinkConfig_Validates_And_Normalizes_Fallbacks()
    {
        var peer = CreatePeer(NodeRole.Dest);
        var link = new ChainLinkConfig(
            Guid.NewGuid(), "to-dest", TransportKind.AmneziaWG, peer, 0, 18443, 1,
            new[] { TransportKind.AmneziaWG, TransportKind.Direct, TransportKind.Direct });

        link.FallbackTransports.Should().Equal(TransportKind.Direct);
        link.WithTransport(TransportKind.Direct).Transport.Should().Be(TransportKind.Direct);

        var badPort = () => new ChainLinkConfig(Guid.NewGuid(), "x", TransportKind.Direct, peer, 0, 0);
        var badTransport = () => new ChainLinkConfig(Guid.NewGuid(), "x", (TransportKind)99, peer, 0, 1);
        badPort.Should().Throw<ArgumentOutOfRangeException>();
        badTransport.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies that a peer signing key must be Base64.
    /// </summary>
    [Fact]
    public void PeerDescriptor_Rejects_Invalid_Signing_Key()
    {
        var create = () => new PeerDescriptor(new NodeId("p"), "p", NodeRole.Dest, "host", 22, "user", signingPublicKey: "not base64!");
        create.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies file change validation.
    /// </summary>
    [Fact]
    public void FileChange_Validates_Hash_And_Size()
    {
        new FileChange(ChangeKind.Modified, "a.txt", Hash.ToUpperInvariant(), 3, false).Sha256.Should().Be(Hash);
        var badHash = () => new FileChange(ChangeKind.Added, "a.txt", "abc", 1, false);
        var badSize = () => new FileChange(ChangeKind.Added, "a.txt", Hash, -1, false);
        badHash.Should().Throw<ArgumentException>();
        badSize.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Verifies envelope signing copies and metadata transitions.
    /// </summary>
    [Fact]
    public void PatchEnvelope_And_Metadata_Transitions()
    {
        var envelope = CreateEnvelope();
        envelope.IsSigned.Should().BeFalse();

        var signed = envelope.WithSignature(new byte[] { 1, 2, 3 });
        signed.IsSigned.Should().BeTrue();
        signed.Id.Should().Be(envelope.Id);

        var metadata = PatchMetadata.FromEnvelope(signed, PatchDirection.Incoming);
        metadata.Status.Should().Be(PatchStatus.Pending);

        var conflict = metadata.WithResult(PatchApplyResult.Conflict(envelope.Id, new[] { new ConflictFile("a.txt", Hash, null) }), DateTimeOffset.UtcNow);
        conflict.Status.Should().Be(PatchStatus.ConflictPending);
        conflict.AppliedAtUtc.Should().BeNull();
        conflict.Conflicts.Should().HaveCount(1);

        var applied = conflict.WithResult(PatchApplyResult.Applied(envelope.Id, 1), DateTimeOffset.UtcNow);
        applied.Status.Should().Be(PatchStatus.Applied);
        applied.AppliedAtUtc.Should().NotBeNull();
        applied.Conflicts.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the content a receiver needs lists every distinct hash once and skips deletions.
    /// </summary>
    [Fact]
    public void PatchEnvelope_ContentHashes_Are_Distinct_And_Skip_Deletions()
    {
        var other = new string('a', 64);
        var envelope = new PatchEnvelope(
            Guid.NewGuid(),
            new NodeId("source"),
            new NodeId("dest"),
            DateTimeOffset.UtcNow,
            Hash,
            Hash,
            new[]
            {
                new FileChange(ChangeKind.Added, "a.txt", Hash, 1, false),
                new FileChange(ChangeKind.Added, "copy-of-a.txt", Hash, 1, false),
                new FileChange(ChangeKind.Modified, "b.txt", other, 2, false),
                new FileChange(ChangeKind.Deleted, "gone.txt", null, null, false)
            });

        envelope.ContentHashes.Should().Equal(Hash, other);
    }

    /// <summary>
    /// Verifies apply result helpers.
    /// </summary>
    [Fact]
    public void PatchApplyResult_Helpers()
    {
        var id = Guid.NewGuid();
        PatchApplyResult.Applied(id, 2).Succeeded.Should().BeTrue();
        PatchApplyResult.Failed(id, "boom").Succeeded.Should().BeFalse();
        PatchApplyResult.Conflict(id, new[] { new ConflictFile("x", null, null) }).HasConflicts.Should().BeTrue();
        var negative = () => new PatchApplyResult(id, PatchStatus.Applied, -1, Array.Empty<ConflictFile>(), null);
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>
    /// Verifies that events round-trip through System.Text.Json with string enums and string node identifiers.
    /// </summary>
    [Fact]
    public void Events_RoundTrip_Json()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var linkEvent = new LinkStateChangedEvent(new LinkState(Guid.NewGuid(), "to-dest", TransportKind.AmneziaWG, LinkStatus.Degraded, "fallback", DateTimeOffset.UtcNow));
        var diagnostics = new DiagnosticsEvent(
            NodeRole.Dest,
            new[] { DiagnosticResult.Fail("GitInstalled", "missing", "install git", new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "create", new Dictionary<string, string> { ["path"] = "/x" })) },
            DateTimeOffset.UtcNow);

        var linkJson = JsonSerializer.Serialize(linkEvent, options);
        linkJson.Should().Contain("\"AmneziaWG\"").And.Contain("\"Degraded\"");
        JsonSerializer.Deserialize<LinkStateChangedEvent>(linkJson, options)!.State.Status.Should().Be(LinkStatus.Degraded);

        var roundTrip = JsonSerializer.Deserialize<DiagnosticsEvent>(JsonSerializer.Serialize(diagnostics, options), options)!;
        roundTrip.AllPassed.Should().BeFalse();
        roundTrip.Results[0].AutoFix!.Parameters["path"].Should().Be("/x");

        var entry = new SyncHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, PatchDirection.Outgoing, new NodeId("s"), new NodeId("d"), Guid.NewGuid(), PatchStatus.Applied, 1, 0, null);
        var entryJson = JsonSerializer.Serialize(entry, options);
        entryJson.Should().Contain("\"sourceNode\":\"s\"");
        JsonSerializer.Deserialize<SyncHistoryEntry>(entryJson, options)!.TargetNode.Should().Be(new NodeId("d"));
    }

    /// <summary>
    /// Creates a peer descriptor with the given role.
    /// </summary>
    /// <param name="role">The peer role.</param>
    /// <returns>The peer.</returns>
    internal static PeerDescriptor CreatePeer(NodeRole role)
        => new(new NodeId("peer-" + role.ToString().ToLowerInvariant()), "Peer " + role, role, "host-" + role.ToString().ToLowerInvariant(), 22, "rcynq");

    /// <summary>
    /// Creates an unsigned envelope with one modified file.
    /// </summary>
    /// <returns>The envelope.</returns>
    private static PatchEnvelope CreateEnvelope()
        => new(
            Guid.NewGuid(),
            new NodeId("source"),
            new NodeId("dest"),
            DateTimeOffset.UtcNow,
            Hash,
            Hash,
            new[] { new FileChange(ChangeKind.Modified, "a.txt", Hash, 1, false) });
}
