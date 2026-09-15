using FluentAssertions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Repositories;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using Xunit;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Integration tests for the EF Core repositories against a migrated SQLite database.
/// </summary>
public sealed class RepositoryTests : IDisposable
{
    /// <summary>
    /// A valid SHA-256 digest.
    /// </summary>
    private const string Hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    /// <summary>
    /// The test database.
    /// </summary>
    private readonly TestDatabase _database = new();

    /// <summary>
    /// Verifies node upsert, read, update and delete.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task NodeRepository_Upsert_And_Read()
    {
        var repository = new NodeRepository(_database.ContextFactory, TimeProvider.System);
        var node = new NodeConfig(new NodeId("dest-1"), "Dest", NodeRole.Dest, "/repo", null);

        await repository.UpsertAsync(node);
        (await repository.GetAsync(node.Id)).Should().Be(node);

        var renamed = new NodeConfig(node.Id, "Dest renamed", NodeRole.Dest, "/repo2", "/work");
        await repository.UpsertAsync(renamed);
        (await repository.GetAllAsync()).Should().ContainSingle().Which.Should().Be(renamed);

        (await repository.DeleteAsync(node.Id)).Should().BeTrue();
        (await repository.GetAsync(node.Id)).Should().BeNull();
    }

    /// <summary>
    /// Verifies that node state is stored in the local node row and survives configuration upserts.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task NodeStateStore_Persists_State()
    {
        var node = new NodeConfig(new NodeId("source-1"), "Source", NodeRole.Source, null, "/work");
        var store = new NodeStateStore(_database.ContextFactory, node, TimeProvider.System);

        (await store.GetAsync()).Should().Be(new SyncState(null, null, null, null));

        var sent = new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(3));
        await store.SaveAsync(new SyncState("local", "base", sent, null));
        await new NodeRepository(_database.ContextFactory, TimeProvider.System).UpsertAsync(node);

        var state = await store.GetAsync();
        state.LocalStateHash.Should().Be("local");
        state.LastSentAtUtc.Should().Be(sent);
        state.LastSentAtUtc!.Value.Offset.Should().Be(TimeSpan.Zero);
    }

    /// <summary>
    /// Verifies chain link upsert with peer, ordering, JSON columns and delete.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChainLinkRepository_Upsert_And_Read()
    {
        var repository = new ChainLinkRepository(_database.ContextFactory, TimeProvider.System);
        var mediator = new PeerDescriptor(new NodeId("mediator-1"), "Relay", NodeRole.Mediator, "relay.example", 2222, "jump", "SHA256:abc");
        var dest = new PeerDescriptor(new NodeId("dest-1"), "Dest", NodeRole.Dest, "10.0.0.2", 22, "rcynq", null, Convert.ToBase64String(new byte[] { 1, 2 }), "/srv/repo", "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:dest-1");
        var endpoint = new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, dest, 0, 18443, 10);
        var hop = new ChainLinkConfig(
            Guid.NewGuid(), "hop", TransportKind.AmneziaWG, mediator, 0, 22, 0,
            new[] { TransportKind.Direct },
            new Dictionary<string, string> { ["configPath"] = "/etc/amnezia/awg0.conf" });

        await repository.UpsertAsync(endpoint);
        await repository.UpsertAsync(hop);

        var all = await repository.GetAllAsync();
        all.Select(link => link.Name).Should().Equal("hop", "endpoint");
        all[0].FallbackTransports.Should().Equal(TransportKind.Direct);
        all[0].TransportSettings["CONFIGPATH"].Should().Be("/etc/amnezia/awg0.conf");
        all[1].Peer.Should().Be(dest);

        var moved = new ChainLinkConfig(endpoint.Id, "endpoint", TransportKind.AmneziaWG, dest, 18000, 18443, 20);
        await repository.UpsertAsync(moved);
        (await repository.GetAsync(endpoint.Id))!.LocalPort.Should().Be(18000);

        (await repository.DeleteAsync(hop.Id)).Should().BeTrue();
        (await repository.GetAllAsync()).Should().ContainSingle();
        IPeerRepository peers = repository;
        (await peers.GetAllAsync()).Should().HaveCount(2, "deleting a link keeps its peer");
        (await peers.GetAsync(mediator.Id)).Should().Be(mediator);
        (await peers.GetAsync(dest.Id))!.SshPublicKey.Should().Be(dest.SshPublicKey);
    }

    /// <summary>
    /// Verifies patch metadata storage including manifest, conflicts, signature and ordering.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchRepository_Stores_Metadata()
    {
        var repository = new PatchRepository(_database.ContextFactory);
        var older = CreatePatch(DateTimeOffset.UtcNow.AddMinutes(-5));
        var newer = CreatePatch(DateTimeOffset.UtcNow).WithResult(
            PatchApplyResult.Conflict(Guid.Empty, new[] { new ConflictFile("src/a.cs", Hash, null, "local edit") }),
            DateTimeOffset.UtcNow);

        await repository.UpsertAsync(older);
        await repository.UpsertAsync(newer);

        (await repository.ExistsAsync(older.Id)).Should().BeTrue();
        (await repository.ExistsAsync(Guid.NewGuid())).Should().BeFalse();

        var recent = await repository.GetRecentAsync(1);
        recent.Should().ContainSingle().Which.Id.Should().Be(newer.Id);

        var loaded = (await repository.GetAsync(newer.Id))!;
        loaded.Status.Should().Be(PatchStatus.ConflictPending);
        loaded.Manifest.Should().BeEquivalentTo(newer.Manifest);
        loaded.Conflicts.Should().ContainSingle().Which.Detail.Should().Be("local edit");
        loaded.Signature.Should().Equal(7, 7, 7);
        (await repository.GetAllAsync()).Select(patch => patch.Id).Should().Equal(newer.Id, older.Id);
    }

    /// <summary>
    /// Verifies snapshot and history repositories.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Snapshot_And_History_Repositories()
    {
        var snapshots = new SnapshotRepository(_database.ContextFactory);
        var node = new NodeId("source-1");
        await snapshots.CreateAsync(new SnapshotInfo(Guid.NewGuid(), node, DateTimeOffset.UtcNow.AddHours(-1), "old", null));
        var latest = await snapshots.CreateAsync(new SnapshotInfo(Guid.NewGuid(), node, DateTimeOffset.UtcNow, "new", "baseline"));
        (await snapshots.GetLatestAsync(node))!.StateHash.Should().Be("new");
        (await snapshots.DeleteAsync(latest.Id)).Should().BeTrue();
        (await snapshots.GetLatestAsync(node))!.StateHash.Should().Be("old");

        var history = new SyncHistoryRepository(_database.ContextFactory);
        var entry = new SyncHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, PatchDirection.Incoming, new NodeId("s"), new NodeId("d"), Guid.NewGuid(), PatchStatus.Applied, 3, 0, "ok");
        await history.AddAsync(entry);
        (await history.GetRecentAsync(10)).Should().ContainSingle().Which.Should().BeEquivalentTo(entry);
    }

    /// <summary>
    /// Verifies that diagnostics runs round-trip including proposed fixes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task DiagnosticRunRepository_Stores_Runs()
    {
        var repository = new DiagnosticRunRepository(_database.ContextFactory);
        var fix = new DiagnosticFix(DiagnosticFixKind.CreateDirectory, "Create workdir", new Dictionary<string, string> { ["path"] = "/work" });
        await repository.AddAsync(new Core.Events.DiagnosticsEvent(NodeRole.Source, new[] { DiagnosticResult.Pass("A"), DiagnosticResult.Fail("B", "missing", "create it", fix) }, DateTimeOffset.UtcNow));

        var run = (await repository.GetRecentAsync(5)).Should().ContainSingle().Subject;

        run.Role.Should().Be(NodeRole.Source);
        run.AllPassed.Should().BeFalse();
        run.Results[1].AutoFix!.Parameters["path"].Should().Be("/work");
    }

    /// <summary>
    /// Deletes the test database.
    /// </summary>
    public void Dispose() => _database.Dispose();

    /// <summary>
    /// Creates outgoing patch metadata created at the given time.
    /// </summary>
    /// <param name="createdAt">The creation time.</param>
    /// <returns>The metadata.</returns>
    private static PatchMetadata CreatePatch(DateTimeOffset createdAt)
        => new(
            Guid.NewGuid(),
            PatchDirection.Outgoing,
            new NodeId("source-1"),
            new NodeId("dest-1"),
            createdAt,
            Hash,
            Hash,
            PatchStatus.Pending,
            null,
            new[] { new FileChange(ChangeKind.Modified, "src/a.cs", Hash, 10, false), new FileChange(ChangeKind.Deleted, "old.txt", null, null, false) },
            null,
            null,
            new byte[] { 7, 7, 7 });
}
