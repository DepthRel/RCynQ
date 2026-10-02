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
    /// Serializes the changes of the stored state, see <see cref="UpdateAsync"/>.
    /// </summary>
    private readonly SemaphoreSlim _stateGate = new(1, 1);

    /// <summary>The result of the last capture, see <see cref="Latest"/>.</summary>
    private volatile LatestSnapshot? _latest;

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
    /// Gets the result of the last <see cref="CaptureAsync"/>, or <see langword="null"/> when the working tree has not
    /// been looked at since the start.
    /// </summary>
    public SyncSnapshot? Latest => _latest is { } latest && latest.RepositoryId == CurrentRepositoryId ? latest.Snapshot : null;

    /// <summary>
    /// Gets the identifier of the repository the node works with now.
    /// </summary>
    private Guid CurrentRepositoryId => _paths.Repository?.Id ?? RepositoryEntry.DefaultId;

    /// <summary>
    /// Remembers the result of a look at the working tree together with the repository it was taken of.
    /// </summary>
    /// <param name="repositoryId">The repository that was looked at.</param>
    /// <param name="snapshot">The result.</param>
    /// <returns>The result.</returns>
    private SyncSnapshot Remember(Guid repositoryId, SyncSnapshot snapshot)
    {
        _latest = new LatestSnapshot(repositoryId, snapshot);
        return snapshot;
    }

    /// <summary>
    /// The result of a look at the working tree and the repository it belongs to.
    /// </summary>
    /// <param name="RepositoryId">The repository.</param>
    /// <param name="Snapshot">The result.</param>
    private sealed record LatestSnapshot(Guid RepositoryId, SyncSnapshot Snapshot);

    /// <summary>
    /// Brings the sync base up to date, recomputes the state hashes, stores them and finds the pending changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state, the sync base status and the changes the peer does not have yet.</returns>
    public async Task<SyncSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            return Remember(RepositoryEntry.DefaultId, new SyncSnapshot(await _store.GetAsync(cancellationToken), SyncBaseStatus.NotApplicable, Array.Empty<FileChange>(), Array.Empty<ChangedFile>()));
        }

        var repositoryId = CurrentRepositoryId;

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

        // The stored state is read only now, not before the scan: the scan may take long, and a patch sent or received
        // meanwhile has stamped its time, which a state read up front would write back as it was.
        var localHash = _hasher.ComputeHash(local);
        var baseHash = syncBase is null ? null : _hasher.ComputeHash(syncBase);
        var updated = await UpdateAsync(
            stored => new SyncState(localHash, baseHash, stored.LastSentAtUtc, stored.LastReceivedAtUtc),
            cancellationToken);
        return Remember(repositoryId, new SyncSnapshot(updated, status, pending, changed));
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
        var now = _timeProvider.GetUtcNow();
        await UpdateAsync(stored => new SyncState(stored.LocalStateHash, stored.SyncBaseHash, now, stored.LastReceivedAtUtc), cancellationToken);
        return await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Records that a patch was received now and refreshes the state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state.</returns>
    public async Task<SyncState> MarkReceivedAsync(CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow();
        await UpdateAsync(stored => new SyncState(stored.LocalStateHash, stored.SyncBaseHash, stored.LastSentAtUtc, now), cancellationToken);
        return await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Changes the stored state as one step: the state is read, changed and written while no other change of this
    /// tracker runs, so a refresh and a time stamp that happen together never overwrite each other.
    /// </summary>
    /// <param name="change">Builds the new state from the stored one.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored state.</returns>
    private async Task<SyncState> UpdateAsync(Func<SyncState, SyncState> change, CancellationToken cancellationToken)
    {
        await _stateGate.WaitAsync(cancellationToken);
        try
        {
            var updated = change(await _store.GetAsync(cancellationToken));
            await _store.SaveAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _stateGate.Release();
        }
    }
}
