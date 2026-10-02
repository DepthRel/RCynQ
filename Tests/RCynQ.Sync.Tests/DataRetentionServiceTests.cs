using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Paths;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Maintenance;
using RCynQ.Sync.Tests.Fakes;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="DataRetentionService"/> on real data directories.
/// </summary>
public sealed class DataRetentionServiceTests : IDisposable
{
    /// <summary>A valid SHA-256 digest.</summary>
    private static readonly string _hash = new('a', 64);

    /// <summary>The temporary root.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-retention-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The data directories of the node.</summary>
    private readonly AppDataPaths _paths;

    /// <summary>The working tree of the node.</summary>
    private readonly string _workdir;

    /// <summary>The patch repository.</summary>
    private readonly InMemoryPatchRepository _patches = new();

    /// <summary>The patch payload storage on disk.</summary>
    private readonly PatchFileStorage _storage;

    /// <summary>The history repository substitute.</summary>
    private readonly ISyncHistoryRepository _history = Substitute.For<ISyncHistoryRepository>();

    /// <summary>The diagnostics run repository substitute.</summary>
    private readonly IDiagnosticRunRepository _runs = Substitute.For<IDiagnosticRunRepository>();

    /// <summary>The snapshot repository substitute.</summary>
    private readonly ISnapshotRepository _snapshots = Substitute.For<ISnapshotRepository>();

    /// <summary>The service under test.</summary>
    private readonly DataRetentionService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataRetentionServiceTests"/> class.
    /// </summary>
    public DataRetentionServiceTests()
    {
        _workdir = Path.Combine(_root, "work");
        Directory.CreateDirectory(_workdir);
        var appData = new AppDataFactory(AppDataPaths.FromRoot(Path.Combine(_root, "data")));
        _paths = appData.Create();
        _storage = new PatchFileStorage(TestTexts.English, appData);
        _history.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(7);
        _runs.DeleteOlderThanAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(2);
        _service = new DataRetentionService(
            _patches, _storage, _history, _runs, _snapshots, appData,
            new NodeConfig(new NodeId("workstation-1"), "Workstation", NodeRole.Workstation, null, _workdir),
            new WorkingTreeLock(), TimeProvider.System);
    }

    /// <summary>
    /// Verifies that old finished patches go with their stored data, while a patch that waits for a conflict decision
    /// and a recent patch stay with theirs.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Old_Finished_Patches_Are_Removed_With_Their_Data()
    {
        var oldApplied = await AddPatchAsync(DateTimeOffset.UtcNow.AddDays(-20), PatchStatus.Applied);
        var oldConflict = await AddPatchAsync(DateTimeOffset.UtcNow.AddDays(-20), PatchStatus.ConflictPending);
        var recentFailed = await AddPatchAsync(DateTimeOffset.UtcNow.AddDays(-1), PatchStatus.Failed);

        var report = await _service.CleanAsync(TimeSpan.FromDays(10));

        report.Patches.Should().Be(1);
        report.HistoryEntries.Should().Be(7);
        report.DiagnosticRuns.Should().Be(2);
        report.FreedBytes.Should().BeGreaterThan(0);
        _patches.Items.Keys.Should().BeEquivalentTo(new[] { oldConflict, recentFailed });
        (await _storage.ExistsAsync(oldApplied)).Should().BeFalse();
        (await _storage.ExistsAsync(oldConflict)).Should().BeTrue("a conflict that waits for a decision keeps everything");
        (await _storage.ExistsAsync(recentFailed)).Should().BeTrue();
        await _history.Received(1).DeleteOlderThanAsync(Arg.Is<DateTimeOffset>(cutoff => cutoff < DateTimeOffset.UtcNow.AddDays(-9)), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a cleanup by hand (age zero) removes every finished patch but leaves alone a patch that may still be
    /// on its way and one that waits for a decision.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cleanup_Of_Everything_Keeps_What_Is_Not_Finished()
    {
        var failed = await AddPatchAsync(DateTimeOffset.UtcNow.AddMinutes(-5), PatchStatus.Failed);
        var inTransfer = await AddPatchAsync(DateTimeOffset.UtcNow.AddMinutes(-5), PatchStatus.Pending);
        var abandoned = await AddPatchAsync(DateTimeOffset.UtcNow.AddDays(-3), PatchStatus.Pending);
        var conflict = await AddPatchAsync(DateTimeOffset.UtcNow.AddMinutes(-5), PatchStatus.ConflictPending);

        var report = await _service.CleanAsync(TimeSpan.Zero);

        report.Patches.Should().Be(2);
        _patches.Items.Keys.Should().BeEquivalentTo(new[] { inTransfer, conflict });
        (await _storage.ExistsAsync(failed)).Should().BeFalse();
        (await _storage.ExistsAsync(abandoned)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that old backups and temporary leftovers are removed, recent ones stay, and what a dead process left of a
    /// half-applied patch in the working tree is removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Old_Backups_And_Leftovers_Are_Removed()
    {
        var oldBackup = WriteAged(Path.Combine(_paths.BackupsDirectory, "patch-a", "20260101T000000000", "src", "a.cs"), "old backup", days: 30);
        var recentBackup = WriteAged(Path.Combine(_paths.BackupsDirectory, "patch-b", "20261001T000000000", "b.cs"), "recent backup", days: 1);
        var oldTemp = WriteAged(Path.Combine(_paths.TempDirectory, "bundle-old.bundle"), "leftover", days: 30);
        var recentTemp = WriteAged(Path.Combine(_paths.TempDirectory, "bundle-new.bundle"), "in use", days: 0);
        var staging = Path.Combine(_workdir, PathUtility.APPLY_STAGING_PREFIX + "dead");
        WriteAged(Path.Combine(staging, "0.new"), "half a patch", days: 0);
        File.WriteAllText(Path.Combine(_workdir, "kept.txt"), "a file of the user");

        var report = await _service.CleanAsync(TimeSpan.FromDays(10));

        report.Backups.Should().Be(1);
        report.TemporaryItems.Should().Be(2, "one temporary file and one staging directory");
        File.Exists(oldBackup).Should().BeFalse();
        Directory.Exists(Path.Combine(_paths.BackupsDirectory, "patch-a")).Should().BeFalse("an emptied backup directory is removed");
        File.Exists(recentBackup).Should().BeTrue();
        File.Exists(oldTemp).Should().BeFalse();
        File.Exists(recentTemp).Should().BeTrue("something may still be working with a fresh temporary file");
        Directory.Exists(staging).Should().BeFalse();
        File.Exists(Path.Combine(_workdir, "kept.txt")).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that stored data of a patch nobody knows any more is removed once it is old.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stored_Data_Without_A_Record_Is_Removed()
    {
        var orphan = Guid.NewGuid();
        await _storage.SaveDiffAsync(orphan, "diff"u8.ToArray());
        Directory.SetCreationTimeUtc(_storage.GetPatchDirectory(orphan), DateTime.UtcNow.AddDays(-5));
        Directory.SetLastWriteTimeUtc(_storage.GetPatchDirectory(orphan), DateTime.UtcNow.AddDays(-5));
        var fresh = Guid.NewGuid();
        await _storage.SaveDiffAsync(fresh, "diff"u8.ToArray());

        await _service.CleanAsync(TimeSpan.Zero);

        (await _storage.ExistsAsync(orphan)).Should().BeFalse();
        (await _storage.ExistsAsync(fresh)).Should().BeTrue("its record may be written any moment");
    }

    /// <summary>
    /// Deletes the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Stores a patch record with a diff.
    /// </summary>
    /// <param name="createdAt">The creation time.</param>
    /// <param name="status">The status.</param>
    /// <returns>The patch identifier.</returns>
    private async Task<Guid> AddPatchAsync(DateTimeOffset createdAt, PatchStatus status)
    {
        var patch = new PatchMetadata(
            Guid.NewGuid(), PatchDirection.Incoming, new NodeId("origin-1"), new NodeId("workstation-1"), createdAt, _hash, _hash, status, null,
            new[] { new FileChange(ChangeKind.Modified, "a.txt", _hash, 3, false, _hash) });
        await _patches.UpsertAsync(patch);
        await _storage.SaveDiffAsync(patch.Id, "diff --git a/a.txt b/a.txt\n"u8.ToArray());
        return patch.Id;
    }

    /// <summary>
    /// Writes a file and makes it, and the directories of the data root above it, look as old as asked.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="text">The content.</param>
    /// <param name="days">The age in days.</param>
    /// <returns>The file path.</returns>
    private string WriteAged(string path, string text, int days)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        var stamp = DateTime.UtcNow.AddDays(-days);
        File.SetCreationTimeUtc(path, stamp);
        File.SetLastWriteTimeUtc(path, stamp);
        for (var directory = Path.GetDirectoryName(path); directory is not null && directory.Length > _root.Length; directory = Path.GetDirectoryName(directory))
        {
            Directory.SetCreationTimeUtc(directory, stamp);
            Directory.SetLastWriteTimeUtc(directory, stamp);
        }

        return path;
    }
}
