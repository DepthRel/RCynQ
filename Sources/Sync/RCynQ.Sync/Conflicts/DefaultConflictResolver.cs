using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.Conflicts;

/// <summary>
/// <see cref="IConflictResolver"/> implementing the user-selectable conflict policies.
/// </summary>
public sealed class DefaultConflictResolver : IConflictResolver
{
    /// <summary>
    /// The bundle applier.
    /// </summary>
    private readonly BundleOverwriteApplier _bundleApplier;

    /// <summary>
    /// The git applier.
    /// </summary>
    private readonly GitPatchApplier _gitApplier;

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
    /// <param name="bundleApplier">The bundle applier.</param>
    /// <param name="gitApplier">The git applier.</param>
    /// <param name="backups">The backup service.</param>
    /// <param name="paths">The sync paths.</param>
    public DefaultConflictResolver(BundleOverwriteApplier bundleApplier, GitPatchApplier gitApplier, BackupService backups, SyncPaths paths)
    {
        _bundleApplier = bundleApplier;
        _gitApplier = gitApplier;
        _backups = backups;
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ResolveAsync(PatchEnvelope envelope, IReadOnlyList<ConflictFile> conflicts, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(conflicts);
        switch (Guard.Defined(policy, nameof(policy)))
        {
            case ConflictPolicy.KeepLocal:
                return new PatchApplyResult(envelope.Id, PatchStatus.Rejected, 0, Array.Empty<ConflictFile>(), null);

            case ConflictPolicy.ApplyIncomingOverLocal:
                await _backups.BackupAsync(envelope.Id, _paths.Workdir, conflicts.Select(conflict => conflict.Path), cancellationToken);
                var applied = await _bundleApplier.ApplyAsync(envelope, _paths.Workdir, cancellationToken);
                await _bundleApplier.ApplyAsync(envelope, _paths.SyncBase, cancellationToken);
                return new PatchApplyResult(envelope.Id, PatchStatus.ConflictResolved, applied.AppliedFileCount, Array.Empty<ConflictFile>(), null);

            default:
                var gitResult = await _gitApplier.ApplyAsync(envelope, _paths.Workdir, cancellationToken);
                if (gitResult.Status != PatchStatus.Applied)
                {
                    return gitResult;
                }

                await _bundleApplier.ApplyAsync(envelope, _paths.SyncBase, cancellationToken);
                return new PatchApplyResult(envelope.Id, PatchStatus.ConflictResolved, gitResult.AppliedFileCount, Array.Empty<ConflictFile>(), null);
        }
    }
}
