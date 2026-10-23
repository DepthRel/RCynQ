using FluentAssertions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Repositories;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Integration tests for the EF Core repositories against a migrated SQLite database.
/// </summary>
public sealed class RepositoryTests : IDisposable
{
    /// <summary>
    /// A valid SHA-256 digest.
    /// </summary>
    private const string HASH = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

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
        var node = new NodeConfig(new NodeId("origin-1"), "Origin", NodeRole.Origin, "/repo", null);

        await repository.UpsertAsync(node);
        (await repository.GetAsync(node.Id)).Should().Be(node);

        var renamed = new NodeConfig(node.Id, "Origin renamed", NodeRole.Origin, "/repo2", "/work");
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
        var node = new NodeConfig(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, "/work");
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
        var relay = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "relay.example", 2222, "jump", "SHA256:abc");
        var origin = new PeerDescriptor(new NodeId("origin-1"), "Origin", NodeRole.Origin, "10.0.0.2", 22, "rcynq", null, Convert.ToBase64String(new byte[] { 1, 2 }), "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:origin-1");
        var endpoint = new ChainLinkConfig(Guid.NewGuid(), "endpoint", TransportKind.Direct, origin, 0, 18443, 10);
        var hop = new ChainLinkConfig(
            Guid.NewGuid(), "hop", TransportKind.AmneziaWG, relay, 0, 22, 0,
            new[] { TransportKind.Direct },
            new Dictionary<string, string> { ["configPath"] = "/etc/amnezia/awg0.conf" });

        await repository.UpsertAsync(endpoint);
        await repository.UpsertAsync(hop);

        var all = await repository.GetAllAsync();
        all.Select(link => link.Name).Should().Equal("hop", "endpoint");
        all[0].FallbackTransports.Should().Equal(TransportKind.Direct);
        all[0].TransportSettings["CONFIGPATH"].Should().Be("/etc/amnezia/awg0.conf");
        all[1].Peer.Should().Be(origin);

        var moved = new ChainLinkConfig(endpoint.Id, "endpoint", TransportKind.AmneziaWG, origin, 18000, 18443, 20);
        await repository.UpsertAsync(moved);
        (await repository.GetAsync(endpoint.Id))!.LocalPort.Should().Be(18000);

        (await repository.DeleteAsync(hop.Id)).Should().BeTrue();
        (await repository.GetAllAsync()).Should().ContainSingle();
        IPeerRepository peers = repository;
        (await peers.GetAllAsync()).Should().HaveCount(2, "deleting a link keeps its peer");
        (await peers.GetAsync(relay.Id)).Should().Be(relay);
        (await peers.GetAsync(origin.Id))!.SshPublicKey.Should().Be(origin.SshPublicKey);
    }

    /// <summary>
    /// Verifies that the routes of a link and the pinned one are stored with it, that choosing a route stores its
    /// address as the address of the peer, and that a link without routes reads back without any.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChainLinkRepository_Stores_Routes_And_The_Route_In_Use()
    {
        var repository = new ChainLinkRepository(_database.ContextFactory, TimeProvider.System);
        var relay = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "10.8.0.1", 22, "jump");
        var udp = new LinkRoute(Guid.NewGuid(), "udp", relay.Ssh);
        var tcp = new LinkRoute(Guid.NewGuid(), "tcp", new SshEndpoint("jump", "10.9.0.1", 2200), 1);
        var plain = new ChainLinkConfig(Guid.NewGuid(), "plain", TransportKind.Direct, relay, 0, 22, 5);
        var routed = new ChainLinkConfig(Guid.NewGuid(), "routed", TransportKind.Direct, relay, 0, 22, 10, routes: new[] { tcp, udp });

        await repository.UpsertAsync(plain);
        await repository.UpsertAsync(routed);

        (await repository.GetAsync(plain.Id))!.Routes.Should().BeEmpty();
        var stored = (await repository.GetAsync(routed.Id))!;
        stored.Routes.Should().Equal(udp, tcp);
        stored.PinnedRouteId.Should().BeNull();
        stored.ActiveRoute.Should().Be(udp);

        await repository.UpsertAsync(stored.WithPinnedRoute(tcp.Id));

        var pinned = (await repository.GetAsync(routed.Id))!;
        pinned.PinnedRouteId.Should().Be(tcp.Id);
        pinned.ActiveRoute.Should().Be(tcp);
        IPeerRepository peers = repository;
        (await peers.GetAsync(relay.Id))!.Ssh.Should().Be(tcp.Ssh, "the address of the peer is the route in use");
    }

    /// <summary>
    /// Verifies that a peer is forgotten only when no link leads to it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ChainLinkRepository_Forgets_Only_Unlinked_Peers()
    {
        var repository = new ChainLinkRepository(_database.ContextFactory, TimeProvider.System);
        IPeerRepository peers = repository;
        var relay = new PeerDescriptor(new NodeId("relay-1"), "Relay", NodeRole.Relay, "10.8.0.1", 22, "jump");
        var link = new ChainLinkConfig(Guid.NewGuid(), "relay", TransportKind.Direct, relay, 0, 22, 10);
        await repository.UpsertAsync(link);

        (await peers.DeleteUnlinkedAsync(relay.Id)).Should().BeFalse("a link still leads to the peer");
        (await peers.GetAsync(relay.Id)).Should().NotBeNull();

        await repository.DeleteAsync(link.Id);

        (await peers.DeleteUnlinkedAsync(relay.Id)).Should().BeTrue();
        (await peers.GetAsync(relay.Id)).Should().BeNull();
        (await peers.DeleteUnlinkedAsync(relay.Id)).Should().BeFalse();
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
            PatchApplyResult.Conflict(Guid.Empty, new[] { new ConflictFile("src/a.cs", HASH, null, "local edit") }),
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
    /// Verifies that the patches are read page by page, newest first, and that the pages neither repeat nor lose a record,
    /// even for patches created at the same moment.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchRepository_Reads_Pages_Newest_First()
    {
        var repository = new PatchRepository(_database.ContextFactory);
        var now = DateTimeOffset.UtcNow;
        var patches = Enumerable.Range(0, 7).Select(index => CreatePatch(now.AddMinutes(-(index / 2)))).ToArray();
        foreach (var patch in patches)
        {
            await repository.UpsertAsync(patch);
        }

        var first = await repository.GetPageAsync(0, 3);
        var second = await repository.GetPageAsync(3, 3);
        var third = await repository.GetPageAsync(6, 3);

        var all = (await repository.GetAllAsync()).Select(patch => patch.Id).ToArray();
        first.Concat(second).Concat(third).Select(patch => patch.Id).Should().OnlyHaveUniqueItems().And.BeEquivalentTo(all);
        first.Concat(second).Concat(third).Select(patch => patch.CreatedAtUtc).Should().BeInDescendingOrder();
        third.Should().ContainSingle();
        (await repository.GetPageAsync(7, 3)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies snapshot and history repositories.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Snapshot_And_History_Repositories()
    {
        var snapshots = new SnapshotRepository(_database.ContextFactory);
        var node = new NodeId("workstation-1");
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
        await repository.AddAsync(new Core.Events.DiagnosticsEvent(NodeRole.Workstation, new[] { DiagnosticResult.Pass("A"), DiagnosticResult.Fail("B", "missing", "create it", fix) }, DateTimeOffset.UtcNow));

        var run = (await repository.GetRecentAsync(5)).Should().ContainSingle().Subject;

        run.Role.Should().Be(NodeRole.Workstation);
        run.AllPassed.Should().BeFalse();
        run.Results[1].AutoFix!.Parameters["path"].Should().Be("/work");
    }

    /// <summary>
    /// Verifies that patches are counted by direction and state in the database, and that old patches are deleted except
    /// those in a state that is kept.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task PatchRepository_Counts_And_Deletes_Old_Patches()
    {
        var repository = new PatchRepository(_database.ContextFactory);
        var conflict = PatchApplyResult.Conflict(Guid.Empty, new[] { new ConflictFile("src/a.cs", HASH, null, "local edit") });
        var oldPending = CreatePatch(DateTimeOffset.UtcNow.AddDays(-20));
        var oldConflict = CreatePatch(DateTimeOffset.UtcNow.AddDays(-20)).WithResult(conflict, DateTimeOffset.UtcNow);
        var recentConflict = CreatePatch(DateTimeOffset.UtcNow).WithResult(conflict, DateTimeOffset.UtcNow);
        foreach (var patch in new[] { oldPending, oldConflict, recentConflict })
        {
            await repository.UpsertAsync(patch);
        }

        (await repository.CountAsync(PatchDirection.Outgoing, PatchStatus.ConflictPending)).Should().Be(2);
        (await repository.CountAsync(PatchDirection.Incoming, PatchStatus.ConflictPending)).Should().Be(0);

        var deleted = await repository.DeleteOlderThanAsync(DateTimeOffset.UtcNow.AddDays(-10), new[] { PatchStatus.ConflictPending });

        deleted.Should().Equal(oldPending.Id);
        (await repository.GetAllAsync()).Select(patch => patch.Id).Should().BeEquivalentTo(new[] { oldConflict.Id, recentConflict.Id });
    }

    /// <summary>
    /// Verifies that old history entries, diagnostic runs and snapshot records are deleted, and that the newest snapshot
    /// record of a node is kept whatever its age.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Old_History_Runs_And_Snapshots_Are_Deleted()
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-10);
        var history = new SyncHistoryRepository(_database.ContextFactory);
        var recentEntry = new SyncHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, PatchDirection.Incoming, new NodeId("s"), new NodeId("d"), Guid.NewGuid(), PatchStatus.Applied, 3, 0, "ok");
        await history.AddAsync(recentEntry);
        await history.AddAsync(new SyncHistoryEntry(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-30), PatchDirection.Incoming, new NodeId("s"), new NodeId("d"), Guid.NewGuid(), PatchStatus.Applied, 1, 0, "old"));
        var runs = new DiagnosticRunRepository(_database.ContextFactory);
        await runs.AddAsync(new Core.Events.DiagnosticsEvent(NodeRole.Workstation, new[] { DiagnosticResult.Pass("A") }, DateTimeOffset.UtcNow.AddDays(-30)));
        await runs.AddAsync(new Core.Events.DiagnosticsEvent(NodeRole.Workstation, new[] { DiagnosticResult.Pass("B") }, DateTimeOffset.UtcNow));
        var snapshots = new SnapshotRepository(_database.ContextFactory);
        var node = new NodeId("workstation-1");
        await snapshots.CreateAsync(new SnapshotInfo(Guid.NewGuid(), node, DateTimeOffset.UtcNow.AddDays(-40), "oldest", null));
        await snapshots.CreateAsync(new SnapshotInfo(Guid.NewGuid(), node, DateTimeOffset.UtcNow.AddDays(-30), "newest of the node", null));

        (await history.DeleteOlderThanAsync(cutoff)).Should().Be(1);
        (await runs.DeleteOlderThanAsync(cutoff)).Should().Be(1);
        (await snapshots.DeleteOlderThanAsync(cutoff)).Should().Be(1);

        (await history.GetAllAsync()).Should().ContainSingle().Which.Id.Should().Be(recentEntry.Id);
        (await runs.GetRecentAsync(10)).Should().ContainSingle().Which.Results.Single().CheckName.Should().Be("B");
        (await snapshots.GetAllAsync()).Should().ContainSingle().Which.StateHash.Should().Be("newest of the node");
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
            new NodeId("workstation-1"),
            new NodeId("origin-1"),
            createdAt,
            HASH,
            HASH,
            PatchStatus.Pending,
            null,
            new[] { new FileChange(ChangeKind.Modified, "src/a.cs", HASH, 10, false), new FileChange(ChangeKind.Deleted, "old.txt", null, null, false) },
            null,
            null,
            new byte[] { 7, 7, 7 });
}
