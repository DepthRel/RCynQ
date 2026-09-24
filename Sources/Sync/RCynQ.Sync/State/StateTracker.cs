using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Manifest;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.State;

/// <summary>
/// Maintains the persisted <see cref="SyncState"/> of the local node and finds the changes the peer does not have yet.
/// </summary>
/// <remarks>
/// Every capture takes the <see cref="WorkingTreeLock"/> while it brings the sync base up to date and reads it, so it never sees a
/// base that a patch, a Pull or a new export is changing. Callers must therefore not hold the lock themselves.
/// </remarks>
public sealed class StateTracker
{
    /// <summary>
    /// The state store.
    /// </summary>
    private readonly INodeStateStore _store;

    /// <summary>
    /// The manifest builder.
    /// </summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>
    /// The state hasher.
    /// </summary>
    private readonly IStateHasher _hasher;

    /// <summary>
    /// The sync paths.
    /// </summary>
    private readonly SyncPaths _paths;

    /// <summary>
    /// The local node.
    /// </summary>
    private readonly NodeConfig _node;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The sync base manager.
    /// </summary>
    private readonly SyncBaseManager _base;

    /// <summary>
    /// The working tree lock.
    /// </summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>
    /// Initializes a new instance of the <see cref="StateTracker"/> class.
    /// </summary>
    /// <param name="store">The state store.</param>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="node">The local node.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    public StateTracker(
        INodeStateStore store,
        IFileManifestBuilder manifest,
        IStateHasher hasher,
        SyncPaths paths,
        NodeConfig node,
        TimeProvider timeProvider,
        SyncBaseManager syncBase,
        WorkingTreeLock workingTreeLock)
    {
        _store = store;
        _manifest = manifest;
        _hasher = hasher;
        _paths = paths;
        _node = node;
        _timeProvider = timeProvider;
        _base = syncBase;
        _lock = workingTreeLock;
    }

    /// <summary>
    /// Gets the persisted state without recomputing it.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state.</returns>
    public Task<SyncState> GetAsync(CancellationToken cancellationToken = default) => _store.GetAsync(cancellationToken);

    /// <summary>
    /// Brings the sync base up to date, recomputes the state hashes, stores them and finds the pending changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state, the sync base status and the changes the peer does not have yet.</returns>
    public async Task<SyncSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(cancellationToken);
        if (!_node.ParticipatesInPatchChannel)
        {
            return new SyncSnapshot(current, SyncBaseStatus.NotApplicable, Array.Empty<FileChange>(), Array.Empty<ChangedFile>());
        }

        SyncBaseStatus status;
        IReadOnlyList<FileEntry> local;
        IReadOnlyList<FileEntry>? syncBase = null;
        IReadOnlyList<FileChange> pending = Array.Empty<FileChange>();
        IReadOnlyList<ChangedFile> changed = Array.Empty<ChangedFile>();
        using (await _lock.AcquireAsync(cancellationToken))
        {
            status = await _base.EnsureAsync(cancellationToken);
            local = await _manifest.ScanAsync(_paths.Workdir, cancellationToken);
            if (status.IsReady)
            {
                // The base is read with the rules of the working tree, see IFileManifestBuilder.BuildAsync.
                syncBase = await _manifest.ScanAsync(_paths.SyncBase, _paths.Workdir, cancellationToken);
                pending = await _base.SelectPendingAsync(DefaultFileManifestBuilder.Compare(syncBase, local), status, cancellationToken);
                changed = await _base.ListChangedAsync(pending, status, cancellationToken);
            }
        }

        var updated = new SyncState(
            _hasher.ComputeHash(local),
            syncBase is null ? null : _hasher.ComputeHash(syncBase),
            current.LastSentAtUtc,
            current.LastReceivedAtUtc);
        await _store.SaveAsync(updated, cancellationToken);
        return new SyncSnapshot(updated, status, pending, changed);
    }

    /// <summary>
    /// Recomputes and stores the state hashes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state.</returns>
    public async Task<SyncState> RefreshAsync(CancellationToken cancellationToken = default)
        => (await CaptureAsync(cancellationToken)).State;

    /// <summary>
    /// Records that a patch was sent now and refreshes the state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state.</returns>
    public async Task<SyncState> MarkSentAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(cancellationToken);
        await _store.SaveAsync(new SyncState(current.LocalStateHash, current.SyncBaseHash, _timeProvider.GetUtcNow(), current.LastReceivedAtUtc), cancellationToken);
        return await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Records that a patch was received now and refreshes the state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state.</returns>
    public async Task<SyncState> MarkReceivedAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(cancellationToken);
        await _store.SaveAsync(new SyncState(current.LocalStateHash, current.SyncBaseHash, current.LastSentAtUtc, _timeProvider.GetUtcNow()), cancellationToken);
        return await RefreshAsync(cancellationToken);
    }
}

/// <summary>
/// The result of <see cref="StateTracker.CaptureAsync"/>.
/// </summary>
/// <param name="State">The stored state.</param>
/// <param name="SyncBase">The status of the sync base.</param>
/// <param name="PendingChanges">The changes of the working tree the peer does not have yet; empty without a sync base.</param>
/// <param name="ChangedFiles">Every file that differs from the Git commit, including those the peer already has; empty without a sync base.</param>
public sealed record SyncSnapshot(SyncState State, SyncBaseStatus SyncBase, IReadOnlyList<FileChange> PendingChanges, IReadOnlyList<ChangedFile> ChangedFiles);
