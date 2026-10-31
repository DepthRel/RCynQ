using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Merging;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Services;

/// <summary>
/// Resolves an incoming patch that waits for a decision, with the policy the user chose.
/// </summary>
/// <remarks>
/// The whole resolution runs under the working tree lock. Once the working tree is being written the resolution is
/// finished and recorded whatever the caller does meanwhile.
/// </remarks>
public sealed class PatchResolveOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The conflict resolver.</summary>
    private readonly IConflictResolver _resolver;

    /// <summary>The conflict detector.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The merge sessions, or <see langword="null"/> when the host keeps none.</summary>
    private readonly MergeSessionStore? _mergeSessions;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchResolveOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="resolver">The conflict resolver.</param>
    /// <param name="detector">The conflict detector.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="mergeSessions">The merge sessions, which end when their patch is decided here; <see langword="null"/> when the host keeps none.</param>
    public PatchResolveOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IConflictResolver resolver,
        ConflictDetector detector,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        SyncBaseManager syncBase,
        MergeSessionStore? mergeSessions = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _mergeSessions = mergeSessions;
        _node = node;
        _paths = paths;
        _resolver = resolver;
        _detector = detector;
        _patches = patches;
        _storage = storage;
        _outcomes = outcomes;
        _tracker = tracker;
        _lock = workingTreeLock;
        _syncBase = syncBase;
    }

    /// <summary>
    /// Resolves a pending conflict using an explicit policy.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolution result.</returns>
    /// <exception cref="SyncBridgeException">The node is outside the patch channel.</exception>
    /// <exception cref="ArgumentException">The policy is not defined.</exception>
    /// <exception cref="KeyNotFoundException">The patch is unknown.</exception>
    /// <exception cref="InvalidOperationException">The patch is not an incoming patch that waits for a decision.</exception>
    public async Task<PatchApplyResult> ExecuteAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentException($"Conflict policy '{policy}' is not defined.", nameof(policy));
        }

        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            // Read under the lock: a second resolution that waited for the first one must see the decided patch.
            var metadata = await _patches.GetAsync(patchId, cancellationToken)
                ?? throw new KeyNotFoundException(_localizer.Format("Patch.NotFound", patchId));
            if (metadata.Direction != PatchDirection.Incoming || metadata.Status is not (PatchStatus.ConflictPending or PatchStatus.Failed))
            {
                throw new InvalidOperationException(_localizer.Format("Patch.NotResolvable", patchId, metadata.Direction, metadata.Status));
            }

            var envelope = new PatchEnvelope(
                metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash,
                metadata.Manifest, metadata.Signature, metadata.Repository);
            try
            {
                var syncBase = await _syncBase.EnsureAsync(cancellationToken);
                if (!syncBase.IsReady)
                {
                    throw new SyncBridgeException(syncBase.Problem ?? _localizer.Get("Patch.NoBaseline"));
                }

                var conflicts = await _detector.DetectAsync(envelope.Manifest, _paths.Workdir, _paths.SyncBase, cancellationToken);

                // From here the working tree and the base are written. That is finished and recorded whatever the caller
                // does meanwhile: a resolution stopped half-way would leave files of both versions and a patch still open.
                result = await _resolver.ResolveAsync(envelope, new StoredPatchContent(patchId, _storage), conflicts, policy, CancellationToken.None);
                if (result.Status is PatchStatus.ConflictResolved or PatchStatus.Rejected)
                {
                    await _syncBase.RecordExchangeAsync(envelope.Manifest.Select(change => change.Path), CancellationToken.None);
                }
            }
            catch (Exception ex) when (ex is PatchApplyException or SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(patchId, _localizer.Describe(ex));
            }

            // A patch that is decided no longer needs its content; one that still waits for a decision keeps it.
            if (result.Status is PatchStatus.ConflictResolved or PatchStatus.Rejected)
            {
                await _storage.DeleteContentAsync(patchId, CancellationToken.None);

                // A merge the user started in a merge tool has nothing to apply to any more.
                _mergeSessions?.RemoveForPatch(patchId);
            }

            await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Patch.ResolvedWith", policy), CancellationToken.None);
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(cancellationToken));
        return result;
    }
}
