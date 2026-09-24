using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Patches.Diff;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.Conflicts;

/// <summary>
/// <see cref="IConflictResolver"/> implementing the user-selectable conflict policies.
/// </summary>
/// <remarks>
/// Whatever the policy, the peer keeps the incoming content, so the sync base takes it once the patch is decided. With
/// <see cref="ConflictPolicy.KeepLocal"/> the local file then differs from the base and is offered to be sent, and the peer can apply
/// that answer without a new conflict: it names exactly what the peer has.
/// </remarks>
public sealed class DefaultConflictResolver : IConflictResolver
{
    /// <summary>
    /// The applier that writes the incoming content.
    /// </summary>
    private readonly ContentOverwriteApplier _contentApplier;

    /// <summary>
    /// The git applier.
    /// </summary>
    private readonly GitPatchApplier _gitApplier;

    /// <summary>
    /// The diff builder; the diff of a patch is built from the local sync base when a policy needs it.
    /// </summary>
    private readonly IPatchDiffBuilder _diffs;

    /// <summary>
    /// The backup service.
    /// </summary>
    private readonly BackupService _backups;

    /// <summary>
    /// The sync paths.
    /// </summary>
    private readonly SyncPaths _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultConflictResolver"/> class.
    /// </summary>
    /// <param name="contentApplier">The applier that writes the incoming content.</param>
    /// <param name="gitApplier">The git applier.</param>
    /// <param name="diffs">The diff builder.</param>
    /// <param name="backups">The backup service.</param>
    /// <param name="paths">The sync paths.</param>
    public DefaultConflictResolver(ContentOverwriteApplier contentApplier, GitPatchApplier gitApplier, IPatchDiffBuilder diffs, BackupService backups, SyncPaths paths)
    {
        _contentApplier = contentApplier;
        _gitApplier = gitApplier;
        _diffs = diffs;
        _backups = backups;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ResolveAsync(PatchEnvelope envelope, IPatchContent content, IReadOnlyList<ConflictFile> conflicts, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(conflicts);
        switch (Guard.Defined(policy, nameof(policy)))
        {
            case ConflictPolicy.KeepLocal:
                try
                {
                    await _contentApplier.ApplyAsync(envelope, content, _paths.SyncBase, cancellationToken);
                }
                catch (Exception ex) when (ex is SyncBridgeException or IOException)
                {
                    // The content of a patch that failed early may be gone; rejecting it must still work, the base just stays as it was.
                }

                return new PatchApplyResult(envelope.Id, PatchStatus.Rejected, 0, Array.Empty<ConflictFile>(), null);

            case ConflictPolicy.ApplyIncomingOverLocal:
                await _backups.BackupAsync(envelope.Id, _paths.Workdir, conflicts.Select(conflict => conflict.Path), cancellationToken);
                var applied = await _contentApplier.ApplyAsync(envelope, content, _paths.Workdir, cancellationToken);
                await _contentApplier.ApplyAsync(envelope, content, _paths.SyncBase, cancellationToken);
                return new PatchApplyResult(envelope.Id, PatchStatus.ConflictResolved, applied.AppliedFileCount, Array.Empty<ConflictFile>(), null);

            default:
                // The sync base still holds the state the patch was built from, because the patch was not applied yet.
                var diff = await _diffs.BuildAsync(envelope.Manifest, _paths.SyncBase, content, PatchDiffLimits.Full, cancellationToken);
                var gitResult = await _gitApplier.ApplyDiffAsync(envelope, diff, _paths.Workdir, cancellationToken);
                if (gitResult.Status != PatchStatus.Applied)
                {
                    return gitResult;
                }

                await _contentApplier.ApplyAsync(envelope, content, _paths.SyncBase, cancellationToken);
                return new PatchApplyResult(envelope.Id, PatchStatus.ConflictResolved, gitResult.AppliedFileCount, Array.Empty<ConflictFile>(), null);
        }
    }
}
