using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Sync.Backups;

namespace RCynQ.Sync.Apply;

/// <summary>
/// Writes the outcome of a merge into the working tree: the local versions are backed up first, then every file is
/// written in one transaction.
/// </summary>
public sealed class MergeOutcomeWriter
{
    /// <summary>Writes the files in one transaction.</summary>
    private readonly ContentOverwriteApplier _applier;

    /// <summary>Backs up the local versions before they are replaced.</summary>
    private readonly BackupService _backups;

    /// <summary>
    /// Initializes a new instance of the <see cref="MergeOutcomeWriter"/> class.
    /// </summary>
    /// <param name="applier">Writes the files in one transaction.</param>
    /// <param name="backups">Backs up the local versions before they are replaced.</param>
    public MergeOutcomeWriter(ContentOverwriteApplier applier, BackupService backups)
    {
        _applier = applier ?? throw new ArgumentNullException(nameof(applier));
        _backups = backups ?? throw new ArgumentNullException(nameof(backups));
    }

    /// <summary>
    /// Writes the outcome.
    /// </summary>
    /// <param name="envelope">The patch the outcome belongs to.</param>
    /// <param name="content">The content of the patch.</param>
    /// <param name="manifest">The changes to write: incoming ones as they are, merged ones with the hash of the merged file.</param>
    /// <param name="merged">The merged files by the path of their change.</param>
    /// <param name="root">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the outcome is written.</returns>
    /// <exception cref="PatchApplyException">The outcome could not be written; the working tree is as it was.</exception>
    public async Task WriteAsync(
        PatchEnvelope envelope,
        IPatchContent content,
        IReadOnlyList<FileChange> manifest,
        IReadOnlyDictionary<string, string> merged,
        string root,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(merged);
        if (manifest.Count == 0)
        {
            return;
        }

        await _backups.BackupAsync(envelope.Id, root, manifest.Select(change => change.Path), cancellationToken);
        var outcome = new PatchEnvelope(
            envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, manifest, repository: envelope.Repository);
        await _applier.ApplyAsync(outcome, new MergedPatchContent(content, merged), root, cancellationToken);
    }
}
