using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Services;

/// <summary>
/// Default <see cref="IPatchService"/>: the single entry point of the patch channel for the API and the CLI.
/// </summary>
/// <remarks>
/// The service itself only reads. Sending is done by <see cref="PatchSendOperation"/>, receiving by
/// <see cref="IPatchReceiver"/> and resolving a conflict by <see cref="PatchResolveOperation"/>.
/// </remarks>
public sealed class PatchService : IPatchService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>Sends the local changes.</summary>
    private readonly PatchSendOperation _send;

    /// <summary>Resolves a patch that waits for a decision.</summary>
    private readonly PatchResolveOperation _resolve;

    /// <summary>The receiver.</summary>
    private readonly IPatchReceiver _receiver;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="send">Sends the local changes.</param>
    /// <param name="resolve">Resolves a patch that waits for a decision.</param>
    /// <param name="receiver">The receiver.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="syncBase">The sync base manager.</param>
    public PatchService(
        NodeConfig node,
        PatchSendOperation send,
        PatchResolveOperation resolve,
        IPatchReceiver receiver,
        IPatchRepository patches,
        IPatchFileStorage storage,
        StateTracker tracker,
        SyncBaseManager syncBase)
    {
        _node = node;
        _send = send;
        _resolve = resolve;
        _receiver = receiver;
        _patches = patches;
        _storage = storage;
        _tracker = tracker;
        _syncBase = syncBase;
    }

    /// <inheritdoc />
    public Task<SendPatchResult> SendAsync(IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default)
        => _send.ExecuteAsync(paths, cancellationToken);

    /// <inheritdoc />
    public Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.OpenSessionAsync(envelope, context, cancellationToken);

    /// <inheritdoc />
    public Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.ReceiveContentAsync(patchId, sha256, content, context, cancellationToken);

    /// <inheritdoc />
    public Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.CommitAsync(patchId, context, cancellationToken);

    /// <inheritdoc />
    public Task<PatchApplyResult> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
        => _resolve.ExecuteAsync(patchId, policy, cancellationToken);

    /// <inheritdoc />
    public async Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _tracker.CaptureAsync(cancellationToken);
        var conflicts = await _patches.CountAsync(PatchDirection.Incoming, PatchStatus.ConflictPending, cancellationToken);
        return new NodeSyncStatus(_node.Id, _node.Name, _node.Role, snapshot.State, snapshot.SyncBase, snapshot.PendingChanges, conflicts, snapshot.ChangedFiles);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        => _patches.GetRecentAsync(count, cancellationToken);

    /// <inheritdoc />
    public Task<PatchMetadata?> GetAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _patches.GetAsync(patchId, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _storage.ReadDiffAsync(patchId, cancellationToken);

    /// <inheritdoc />
    public async Task<SnapshotInfo> RebuildBaselineAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _syncBase.RebuildAsync(cancellationToken);
        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(cancellationToken));
        return snapshot;
    }
}
