using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.History;
using RCynQ.Sync.Paths;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Makes the apply attempt of an incoming patch whose content has arrived in full.
/// </summary>
/// <remarks>
/// The attempt is all-or-nothing: when any file conflicts with a local change nothing is written and the patch waits for
/// a decision; otherwise the content is written to the working tree and to the sync base. The caller holds the working
/// tree lock and records the outcome.
/// </remarks>
public sealed class IncomingPatchApplier
{
    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The store of the diffs shown to the user.</summary>
    private readonly PatchDisplayDiffStore _displayDiffs;

    /// <summary>The conflict detector.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The content applier.</summary>
    private readonly ContentOverwriteApplier _applier;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncomingPatchApplier"/> class.
    /// </summary>
    /// <param name="storage">The payload storage.</param>
    /// <param name="displayDiffs">The store of the diffs shown to the user.</param>
    /// <param name="detector">The conflict detector.</param>
    /// <param name="applier">The content applier.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="syncBase">The sync base manager.</param>
    public IncomingPatchApplier(
        IPatchFileStorage storage,
        PatchDisplayDiffStore displayDiffs,
        ConflictDetector detector,
        ContentOverwriteApplier applier,
        SyncPaths paths,
        SyncBaseManager syncBase)
    {
        _storage = storage;
        _displayDiffs = displayDiffs;
        _detector = detector;
        _applier = applier;
        _paths = paths;
        _syncBase = syncBase;
    }

    /// <summary>
    /// Stages the content of a patch and applies it to the working tree and the sync base, unless a file conflicts.
    /// </summary>
    /// <param name="envelope">The patch; all of its content has been uploaded.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The outcome: applied, waiting for a decision because of conflicts, or failed. A failure of the attempt is part of
    /// the result, not an exception, so the caller can record it.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    public async Task<PatchApplyResult> ApplyAsync(PatchEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        try
        {
            await _storage.StageContentAsync(envelope.Id, envelope.ContentHashes.ToArray(), cancellationToken);
            var content = new StoredPatchContent(envelope.Id, _storage);
            var syncBase = await _syncBase.EnsureAsync(cancellationToken);
            if (!syncBase.IsReady)
            {
                return PatchApplyResult.Failed(envelope.Id, LocalizedText.Encode("Receive.NoBaseline", syncBase.Problem ?? string.Empty));
            }

            await _displayDiffs.SaveAsync(envelope, content, _paths.SyncBase, cancellationToken);
            var conflicts = await _detector.DetectAsync(envelope.Manifest, _paths.Workdir, _paths.SyncBase, cancellationToken);
            if (conflicts.Count > 0)
            {
                return PatchApplyResult.Conflict(envelope.Id, conflicts);
            }

            var result = await _applier.ApplyAsync(envelope, content, _paths.Workdir, cancellationToken);

            // Both nodes now have the incoming content, so it becomes the base on this side too.
            await _applier.ApplyAsync(envelope, content, _paths.SyncBase, cancellationToken);
            await _syncBase.RecordExchangeAsync(envelope.Manifest.Select(change => change.Path), cancellationToken);
            return result;
        }
        catch (Exception ex) when (ex is PatchApplyException or SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return PatchApplyResult.Failed(envelope.Id, ex.Message);
        }
    }
}
