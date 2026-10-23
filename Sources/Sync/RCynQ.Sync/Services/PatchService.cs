using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Localization;
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

    /// <summary>Tells which repository the node works with now, or <see langword="null"/> for the one it started with.</summary>
    private readonly IActiveRepository? _repository;

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
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one the node started with.</param>
    public PatchService(
        NodeConfig node,
        PatchSendOperation send,
        PatchResolveOperation resolve,
        IPatchReceiver receiver,
        IPatchRepository patches,
        IPatchFileStorage storage,
        StateTracker tracker,
        SyncBaseManager syncBase,
        IActiveRepository? repository = null)
    {
        _repository = repository;
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
    public Task<PatchSession> OpenCommitSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.OpenCommitSessionAsync(envelope, context, cancellationToken);

    /// <inheritdoc />
    public Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.ReceiveContentAsync(patchId, sha256, content, context, cancellationToken);

    /// <inheritdoc />
    public Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.CommitAsync(patchId, context, cancellationToken);

    /// <inheritdoc />
    public async Task<PatchApplyResult> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        // A patch is resolved in the working tree it was sent for.
        if (await _patches.GetAsync(patchId, cancellationToken) is { } patch && !Belongs(patch))
        {
            throw new InvalidOperationException(LocalizedError.Message("Repository.PatchOfAnother", patchId, patch.Repository ?? string.Empty));
        }

        return await _resolve.ExecuteAsync(patchId, policy, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await _tracker.CaptureAsync(cancellationToken);
        var conflicts = await CountConflictsAsync(cancellationToken);
        return new NodeSyncStatus(_node.Id, _node.Name, _node.Role, snapshot.State, snapshot.SyncBase, snapshot.PendingChanges, conflicts, snapshot.ChangedFiles);
    }

    /// <inheritdoc />
    public async Task<NodeSyncStatus> GetSummaryAsync(CancellationToken cancellationToken = default)
    {
        if (_tracker.Latest is not { } latest)
        {
            return await GetStateAsync(cancellationToken);
        }

        var state = await _tracker.GetAsync(cancellationToken);
        var conflicts = await CountConflictsAsync(cancellationToken);
        return new NodeSyncStatus(_node.Id, _node.Name, _node.Role, state, latest.SyncBase, latest.PendingChanges, conflicts, latest.ChangedFiles);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        => GetPageAsync(0, count, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatchMetadata>> GetPageAsync(int skip, int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(skip);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        if (_repository is null || _repository.Entries.Count <= 1)
        {
            return await _patches.GetPageAsync(skip, count, cancellationToken);
        }

        // The patches of the other repositories are left out, so the stored ones are read in chunks until the page is
        // filled: a page counts only the patches of this repository.
        var page = new List<PatchMetadata>(count);
        var matched = 0;
        for (var offset = 0; page.Count < count; offset += PATCHES_READ_CHUNK)
        {
            var chunk = await _patches.GetPageAsync(offset, PATCHES_READ_CHUNK, cancellationToken);
            foreach (var patch in chunk.Where(Belongs))
            {
                if (matched++ >= skip && page.Count < count)
                {
                    page.Add(patch);
                }
            }

            if (chunk.Count < PATCHES_READ_CHUNK)
            {
                break;
            }
        }

        return page;
    }

    /// <summary>
    /// The most patches read to find those of one repository.
    /// </summary>
    private const int MAX_PATCHES_READ = 2000;

    /// <summary>
    /// How many stored patches are read at a time to fill a page of one repository.
    /// </summary>
    private const int PATCHES_READ_CHUNK = 200;

    /// <summary>
    /// Tells whether a patch belongs to the repository the node works with now.
    /// </summary>
    /// <param name="patch">The patch.</param>
    /// <returns><see langword="true"/> when the patch belongs to that repository, or the node has one repository only.</returns>
    private bool Belongs(PatchMetadata patch)
    {
        if (_repository?.Current is not { } current || _repository.Entries.Count <= 1)
        {
            return true;
        }

        // A patch without a name was exchanged before the node knew the name: it is of the repository the node started with.
        return patch.Repository is null ? current.Id == RepositoryEntry.DefaultId : current.IsNamed(patch.Repository);
    }

    /// <summary>
    /// Counts the incoming patches of the repository the node works with that wait for a decision about a conflict.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of patches.</returns>
    private async Task<int> CountConflictsAsync(CancellationToken cancellationToken)
    {
        if (_repository is null || _repository.Entries.Count <= 1)
        {
            return await _patches.CountAsync(PatchDirection.Incoming, PatchStatus.ConflictPending, cancellationToken);
        }

        var recent = await _patches.GetRecentAsync(MAX_PATCHES_READ, cancellationToken);
        return recent.Count(patch => patch.Direction == PatchDirection.Incoming && patch.Status == PatchStatus.ConflictPending && Belongs(patch));
    }

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
