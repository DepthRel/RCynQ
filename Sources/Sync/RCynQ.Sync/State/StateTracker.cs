using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Events;
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

    /// <summary>Tells the clients that a capture is done, or <see langword="null"/> to tell nobody.</summary>
    private readonly IEventPublisher? _events;

    /// <summary>The number of captures that run now, by the repository they look at.</summary>
    private readonly ConcurrentDictionary<Guid, int> _running = new();

    /// <summary>Guards <see cref="_requested"/>.</summary>
    private readonly object _requestSync = new();

    /// <summary>The capture started by <see cref="RequestCapture"/>, or <see langword="null"/> before the first request.</summary>
    private Task? _requested;

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
    /// <param name="events">Tells the clients that a capture is done; <see langword="null"/> tells nobody.</param>
    public StateTracker(
        INodeStateStore store,
        IFileManifestBuilder manifest,
        IStateHasher hasher,
        SyncPaths paths,
        NodeConfig node,
        TimeProvider timeProvider,
        SyncBaseManager syncBase,
        WorkingTreeLock workingTreeLock,
        IEventPublisher? events = null)
    {
        _store = store;
        _manifest = manifest;
        _hasher = hasher;
        _paths = paths;
        _node = node;
        _timeProvider = timeProvider;
        _base = syncBase;
        _lock = workingTreeLock;
        _events = events;
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
    /// Brings the sync base up to date, recomputes the state hashes, stores them and finds the pending changes, then tells
    /// the clients that the summary of the state is complete.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state, the sync base status and the changes the peer does not have yet.</returns>
    public async Task<SyncSnapshot> CaptureAsync(CancellationToken cancellationToken = default)
    {
        var repositoryId = CurrentRepositoryId;
        _running.AddOrUpdate(repositoryId, 1, (_, count) => count + 1);
        SyncSnapshot snapshot;
        try
        {
            snapshot = await CaptureCoreAsync(cancellationToken);
        }
        finally
        {
            _running.AddOrUpdate(repositoryId, 0, (_, count) => count - 1);
        }

        if (_events is not null)
        {
            await _events.PublishStateCapturedAsync(new StateCapturedEvent(_timeProvider.GetUtcNow()), cancellationToken);
        }

        return snapshot;
    }

    /// <summary>
    /// Has the working tree of the current repository looked at in the background, unless a look at it already runs;
    /// its end is told to the clients like that of every capture.
    /// </summary>
    /// <remarks>
    /// For whoever needs the full state but must not wait for it: the summary asked before the first look after the start
    /// (then the look of the start runs already, and nothing more is started) or after a switch to another repository.
    /// </remarks>
    public void RequestCapture()
    {
        lock (_requestSync)
        {
            var looking = _running.TryGetValue(CurrentRepositoryId, out var count) && count > 0;
            if (looking || _requested is { IsCompleted: false })
            {
                return;
            }

            _requested = Task.Run(CaptureQuietlyAsync);
        }
    }

    /// <summary>
    /// Captures the state for <see cref="RequestCapture"/>, where nobody waits for the outcome.
    /// </summary>
    /// <returns>A task that completes when the capture is done or failed.</returns>
    private async Task CaptureQuietlyAsync()
    {
        try
        {
            await CaptureAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Nobody waits for this look: a working tree that cannot be read is reported when a page asks for the full
            // state, which looks again.
        }
    }

    /// <summary>
    /// Brings the sync base up to date, recomputes the state hashes, stores them and finds the pending changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state, the sync base status and the changes the peer does not have yet.</returns>
    private async Task<SyncSnapshot> CaptureCoreAsync(CancellationToken cancellationToken)
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
