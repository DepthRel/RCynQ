using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.State;

/// <summary>
/// Maintains the persisted <see cref="SyncState"/> of the local node.
/// </summary>
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
    /// Initializes a new instance of the <see cref="StateTracker"/> class.
    /// </summary>
    /// <param name="store">The state store.</param>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="node">The local node.</param>
    /// <param name="timeProvider">The clock.</param>
    public StateTracker(INodeStateStore store, IFileManifestBuilder manifest, IStateHasher hasher, SyncPaths paths, NodeConfig node, TimeProvider timeProvider)
    {
        _store = store;
        _manifest = manifest;
        _hasher = hasher;
        _paths = paths;
        _node = node;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Gets the persisted state without rescanning.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The state.</returns>
    public Task<SyncState> GetAsync(CancellationToken cancellationToken = default) => _store.GetAsync(cancellationToken);

    /// <summary>
    /// Recomputes <see cref="SyncState.LocalStateHash"/> and <see cref="SyncState.SyncBaseHash"/> and persists them.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state; the stored state for a Mediator.</returns>
    public async Task<SyncState> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(cancellationToken);
        if (!_node.ParticipatesInPatchChannel)
        {
            return current;
        }

        var local = _hasher.ComputeHash(await _manifest.ScanAsync(_paths.Workdir, cancellationToken));
        var syncBase = _paths.BaselineInitialized ? _hasher.ComputeHash(await _manifest.ScanAsync(_paths.SyncBase, cancellationToken)) : null;
        var updated = new SyncState(local, syncBase, current.LastSentAtUtc, current.LastReceivedAtUtc);
        await _store.SaveAsync(updated, cancellationToken);
        return updated;
    }

    /// <summary>
    /// Records that a patch was sent and refreshes the hashes.
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
    /// Records that a patch was received and refreshes the hashes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The updated state.</returns>
    public async Task<SyncState> MarkReceivedAsync(CancellationToken cancellationToken = default)
    {
        var current = await _store.GetAsync(cancellationToken);
        await _store.SaveAsync(new SyncState(current.LocalStateHash, current.SyncBaseHash, current.LastSentAtUtc, _timeProvider.GetUtcNow()), cancellationToken);
        return await RefreshAsync(cancellationToken);
    }

    /// <summary>
    /// Lists local changes between the sync base and the working tree.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The pending changes; empty for a Mediator or without baseline.</returns>
    public async Task<IReadOnlyList<FileChange>> GetPendingChangesAsync(CancellationToken cancellationToken = default)
        => _node.ParticipatesInPatchChannel && _paths.BaselineInitialized
            ? await _manifest.BuildAsync(_paths.SyncBase, _paths.Workdir, cancellationToken)
            : Array.Empty<FileChange>();
}
