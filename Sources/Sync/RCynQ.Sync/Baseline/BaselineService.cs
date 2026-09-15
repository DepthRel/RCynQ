using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Baseline;

/// <summary>
/// Creates the sync base snapshot from the current working tree.
/// </summary>
/// <remarks>
/// Run it on Source and Dest while their working trees are identical (for example right after cloning the same
/// commit). The base is built in a staging directory and swapped in afterwards.
/// </remarks>
public sealed class BaselineService
{
    /// <summary>
    /// The local node.
    /// </summary>
    private readonly NodeConfig _node;

    /// <summary>
    /// The sync paths.
    /// </summary>
    private readonly SyncPaths _paths;

    /// <summary>
    /// The manifest builder.
    /// </summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>
    /// The state hasher.
    /// </summary>
    private readonly IStateHasher _hasher;

    /// <summary>
    /// The snapshot repository.
    /// </summary>
    private readonly ISnapshotRepository _snapshots;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>
    /// The state tracker.
    /// </summary>
    private readonly StateTracker _tracker;

    /// <summary>
    /// The working tree lock.
    /// </summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaselineService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="snapshots">The snapshot repository.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="timeProvider">The clock.</param>
    public BaselineService(
        NodeConfig node,
        SyncPaths paths,
        IFileManifestBuilder manifest,
        IStateHasher hasher,
        ISnapshotRepository snapshots,
        IPathUtility pathUtility,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        TimeProvider timeProvider)
    {
        _node = node;
        _paths = paths;
        _manifest = manifest;
        _hasher = hasher;
        _snapshots = snapshots;
        _pathUtility = pathUtility;
        _tracker = tracker;
        _lock = workingTreeLock;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Replaces the sync base with a copy of the working tree.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The recorded snapshot.</returns>
    /// <exception cref="SyncBridgeException">The node is a Mediator or the working tree does not exist.</exception>
    public async Task<SnapshotInfo> CreateBaselineAsync(CancellationToken cancellationToken = default)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new SyncBridgeException("A Mediator node has no sync base.");
        }

        var workdir = _paths.Workdir;
        if (!Directory.Exists(workdir))
        {
            throw new SyncBridgeException($"Working tree '{workdir}' does not exist.");
        }

        SnapshotInfo snapshot;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var entries = await _manifest.ScanAsync(workdir, cancellationToken);
            var target = _paths.SyncBase;
            var staging = target + ".new-" + Guid.NewGuid().ToString("N");
            try
            {
                Directory.CreateDirectory(staging);
                foreach (var entry in entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var source = _pathUtility.ResolveWithinRoot(workdir, entry.Path);
                    var destination = _pathUtility.ResolveWithinRoot(staging, entry.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    if (entry.IsSymlink)
                    {
                        var link = new FileInfo(source).LinkTarget ?? new DirectoryInfo(source).LinkTarget
                            ?? throw new SyncBridgeException($"'{entry.Path}' stopped being a symbolic link during the baseline.");
                        File.CreateSymbolicLink(destination, link);
                    }
                    else
                    {
                        File.Copy(source, destination);
                    }
                }

                if (Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }

                Directory.Move(staging, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SyncBridgeException($"Creating the sync base failed: {ex.Message}", ex);
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }

            snapshot = await _snapshots.CreateAsync(
                new SnapshotInfo(Guid.NewGuid(), _node.Id, _timeProvider.GetUtcNow(), _hasher.ComputeHash(entries), "baseline"),
                cancellationToken);
        }

        await _tracker.RefreshAsync(cancellationToken);
        return snapshot;
    }
}
