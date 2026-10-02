using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Git.Paths;
using RCynQ.Sync.Locking;

namespace RCynQ.Sync.Maintenance;

/// <summary>
/// Removes the data of a node that is finished and old: patch records with their diffs and content, history entries,
/// stored diagnostics runs, backups of overwritten files and leftover temporary files.
/// </summary>
/// <remarks>
/// <para>
/// Nothing a node still needs is removed: a patch that waits for a decision about a conflict stays with its content
/// whatever its age, a patch that is being transferred stays for a day, and the newest snapshot record of the node stays.
/// </para>
/// <para>
/// The service removes the data older than <see cref="SyncBridgeOptions.RetentionDays"/> on its own. The user can
/// remove everything that is finished at once; that is the same operation with an age of zero.
/// </para>
/// </remarks>
public sealed class DataRetentionService
{
    /// <summary>
    /// A patch that has not finished, and what was uploaded for it, is left alone for this long: it may still be on its way.
    /// </summary>
    public static readonly TimeSpan TransferGrace = TimeSpan.FromDays(1);

    /// <summary>
    /// A temporary file is left alone for this long: something may still be working with it.
    /// </summary>
    public static readonly TimeSpan TemporaryGrace = TimeSpan.FromHours(1);

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The patch payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The history repository.</summary>
    private readonly ISyncHistoryRepository _history;

    /// <summary>The diagnostics run repository.</summary>
    private readonly IDiagnosticRunRepository _runs;

    /// <summary>The snapshot repository.</summary>
    private readonly ISnapshotRepository _snapshots;

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The repositories of the node, or <see langword="null"/> for the one it started with.</summary>
    private readonly IActiveRepository? _repositories;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataRetentionService"/> class.
    /// </summary>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The patch payload storage.</param>
    /// <param name="history">The history repository.</param>
    /// <param name="runs">The diagnostics run repository.</param>
    /// <param name="snapshots">The snapshot repository.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="node">The local node.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="repositories">The repositories of the node; <see langword="null"/> for the one the node started with.</param>
    public DataRetentionService(
        IPatchRepository patches,
        IPatchFileStorage storage,
        ISyncHistoryRepository history,
        IDiagnosticRunRepository runs,
        ISnapshotRepository snapshots,
        IAppDataFactory appData,
        NodeConfig node,
        WorkingTreeLock workingTreeLock,
        TimeProvider timeProvider,
        IActiveRepository? repositories = null)
    {
        _repositories = repositories;
        _patches = patches;
        _storage = storage;
        _history = history;
        _runs = runs;
        _snapshots = snapshots;
        _appData = appData;
        _node = node;
        _lock = workingTreeLock;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Removes the finished data that is older than the given age.
    /// </summary>
    /// <param name="olderThan">The age; <see cref="TimeSpan.Zero"/> removes everything that is finished.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What was removed.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="olderThan"/> is negative.</exception>
    public async Task<DataCleanupReport> CleanAsync(TimeSpan olderThan, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(olderThan, TimeSpan.Zero);
        var now = _timeProvider.GetUtcNow();
        var cutoff = now - olderThan;
        var transferCutoff = Earlier(cutoff, now - TransferGrace);
        var paths = _appData.Create();
        long freed = 0;

        // Patches: the finished ones by the age asked for, the unfinished ones only once they cannot finish any more.
        var removed = new List<Guid>();
        removed.AddRange(await _patches.DeleteOlderThanAsync(cutoff, new[] { PatchStatus.ConflictPending, PatchStatus.Pending }, cancellationToken));
        removed.AddRange(await _patches.DeleteOlderThanAsync(transferCutoff, new[] { PatchStatus.ConflictPending }, cancellationToken));
        foreach (var id in removed)
        {
            freed += await _storage.DeletePatchAsync(id, cancellationToken);
        }

        // Stored data of a patch without a record belongs to nothing: its record was removed earlier, or never written.
        foreach (var id in await _storage.ListStoredPatchesAsync(cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _patches.ExistsAsync(id, cancellationToken) && IsOlderThan(Path.Combine(paths.PatchesDirectory, id.ToString("N")), transferCutoff))
            {
                freed += await _storage.DeletePatchAsync(id, cancellationToken);
            }
        }

        await _storage.PruneUploadsAsync(transferCutoff, cancellationToken);
        var history = await _history.DeleteOlderThanAsync(cutoff, cancellationToken);
        var runs = await _runs.DeleteOlderThanAsync(cutoff, cancellationToken);
        await _snapshots.DeleteOlderThanAsync(cutoff, cancellationToken);

        var (backups, backupBytes) = RemoveBackups(paths.BackupsDirectory, cutoff);
        var (temporary, temporaryBytes) = RemoveEntries(paths.TempDirectory, Earlier(cutoff, now - TemporaryGrace));
        var (staging, stagingBytes) = await RemoveApplyLeftoversAsync(paths, cancellationToken);
        freed += backupBytes + temporaryBytes + stagingBytes;

        return new DataCleanupReport(removed.Count, history, runs, backups, temporary + staging, freed);
    }

    /// <summary>
    /// Removes the backups made before a moment. A backup is the directory <c>backups/&lt;patch&gt;/&lt;time&gt;</c>.
    /// </summary>
    /// <param name="backupsDirectory">The backups directory.</param>
    /// <param name="cutoff">Backups made before this moment are removed.</param>
    /// <returns>The number of removed backups and the bytes they held.</returns>
    private static (int Count, long Bytes) RemoveBackups(string backupsDirectory, DateTimeOffset cutoff)
    {
        var count = 0;
        long bytes = 0;
        foreach (var patch in EnumerateDirectories(backupsDirectory))
        {
            var (removed, freed) = RemoveEntries(patch, cutoff);
            count += removed;
            bytes += freed;
            DeleteIfEmpty(patch);
        }

        return (count, bytes);
    }

    /// <summary>
    /// Removes what processes that died left of half-applied patches in the working tree and in the sync base.
    /// </summary>
    /// <param name="paths">The data directories of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of removed directories and the bytes they held.</returns>
    private async Task<(int Count, long Bytes)> RemoveApplyLeftoversAsync(AppDataPaths paths, CancellationToken cancellationToken)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            return (0, 0);
        }

        // While the lock is held no patch is being applied, in this process or in another one: every staging directory
        // that exists now was left behind.
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var count = 0;
            long bytes = 0;
            // Every repository of the node: a staging directory is left behind where a patch was being applied.
            var repositories = (_repositories ?? FixedRepository.FromNode(_node)).Entries;
            var roots = repositories.Select(repository => repository.Path)
                .Concat(repositories.Select(repository => paths.ForRepository(repository.Id).SyncBaseDirectory))
                .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var root in roots)
            {
                foreach (var directory in EnumerateDirectories(root).Where(path => PathUtility.IsApplyStagingName(Path.GetFileName(path))))
                {
                    var freed = TryDelete(directory);
                    if (freed is not null)
                    {
                        count++;
                        bytes += freed.Value;
                    }
                }
            }

            return (count, bytes);
        }
    }

    /// <summary>
    /// Removes the files and directories directly below a directory that were last written before a moment.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="cutoff">Entries written before this moment are removed.</param>
    /// <returns>The number of removed entries and the bytes they held.</returns>
    private static (int Count, long Bytes) RemoveEntries(string directory, DateTimeOffset cutoff)
    {
        var count = 0;
        long bytes = 0;
        if (!Directory.Exists(directory))
        {
            return (count, bytes);
        }

        IEnumerable<string> entries;
        try
        {
            entries = Directory.GetFileSystemEntries(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (count, bytes);
        }

        foreach (var entry in entries)
        {
            if (!IsOlderThan(entry, cutoff))
            {
                continue;
            }

            var freed = TryDelete(entry);
            if (freed is not null)
            {
                count++;
                bytes += freed.Value;
            }
        }

        return (count, bytes);
    }

    /// <summary>
    /// Tells whether a file or directory was created and last written before a moment.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="cutoff">The moment.</param>
    /// <returns><see langword="true"/> when the entry is older; <see langword="false"/> also when it cannot be examined.</returns>
    private static bool IsOlderThan(string path, DateTimeOffset cutoff)
    {
        try
        {
            // The newer of the two stamps, because a file system may not keep a creation time.
            var created = File.GetCreationTimeUtc(path);
            var written = File.GetLastWriteTimeUtc(path);
            return (created > written ? created : written) < cutoff.UtcDateTime;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Deletes a file or a directory with everything in it.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The bytes that were freed, or <see langword="null"/> when the entry could not be deleted.</returns>
    private static long? TryDelete(string path)
    {
        try
        {
            var directory = new DirectoryInfo(path);
            if (directory.Exists)
            {
                // A link is removed as a link; what it points to is not ours.
                if (directory.LinkTarget is not null)
                {
                    directory.Delete();
                    return 0;
                }

                var size = directory.EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }).Sum(file => file.Length);
                directory.Delete(recursive: true);
                return size;
            }

            var file = new FileInfo(path);
            if (!file.Exists)
            {
                return null;
            }

            var length = file.Length;
            file.Delete();
            return length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // In use or not ours to delete; the next cleanup tries again.
            return null;
        }
    }

    /// <summary>
    /// Lists the directories directly below a directory, returning nothing when it is missing or cannot be read.
    /// </summary>
    /// <param name="directory">The directory, or <see langword="null"/>.</param>
    /// <returns>The directories.</returns>
    private static IReadOnlyList<string> EnumerateDirectories(string? directory)
    {
        try
        {
            return directory is not null && Directory.Exists(directory) ? Directory.GetDirectories(directory) : Array.Empty<string>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes a directory when nothing is left in it.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteIfEmpty(string directory)
    {
        try
        {
            if (!Directory.EnumerateFileSystemEntries(directory).Any())
            {
                Directory.Delete(directory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty directory that stays is harmless.
        }
    }

    /// <summary>
    /// Returns the earlier of two moments.
    /// </summary>
    /// <param name="first">The first moment.</param>
    /// <param name="second">The second moment.</param>
    /// <returns>The earlier one.</returns>
    private static DateTimeOffset Earlier(DateTimeOffset first, DateTimeOffset second) => first < second ? first : second;
}
