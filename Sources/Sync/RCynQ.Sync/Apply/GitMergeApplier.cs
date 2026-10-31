using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Localization;
using RCynQ.Sync.Enums;

namespace RCynQ.Sync.Apply;

/// <summary>
/// Merges an incoming patch into a working tree whose files were changed locally: a three-way merge of every file,
/// with the sync base as the common ancestor.
/// </summary>
/// <remarks>
/// <para>
/// A conflict means the receiver changed a file since the last synchronization, and those changes are usually not
/// committed. <c>git apply --3way</c> refuses such a file ("does not match index"), so it cannot do this job. A
/// three-way merge of the file itself can (see <see cref="MergePlanner"/>).
/// </para>
/// <para>
/// The merge is worked out in a temporary directory first. When Git cannot merge one of the files at all (a binary
/// file changed on both sides, a file one side deleted and the other changed), nothing is written and the result names
/// those files, so the user decides with one of the other policies. Otherwise the local versions are backed up and the
/// outcome is written in one transaction. A file with conflict markers is a finished merge that needs an edit, not a
/// failure: it is reported in the result.
/// </para>
/// </remarks>
public sealed class GitMergeApplier
{
    /// <summary>Works out the merge of each file.</summary>
    private readonly MergePlanner _planner;

    /// <summary>Writes the outcome of the merge.</summary>
    private readonly MergeOutcomeWriter _writer;

    /// <summary>The node data directories, or <see langword="null"/> to work in the system temporary directory.</summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitMergeApplier"/> class.
    /// </summary>
    /// <param name="planner">Works out the merge of each file.</param>
    /// <param name="writer">Writes the outcome of the merge.</param>
    /// <param name="appData">The node data directories, or <see langword="null"/> to work in the system temporary directory.</param>
    public GitMergeApplier(MergePlanner planner, MergeOutcomeWriter writer, IAppDataFactory? appData = null)
    {
        _planner = planner ?? throw new ArgumentNullException(nameof(planner));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _appData = appData;
    }

    /// <summary>
    /// Merges a patch into a working tree.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <param name="content">The source of the incoming content.</param>
    /// <param name="baseDirectory">The sync base: the state both sides started from.</param>
    /// <param name="targetDirectory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see cref="PatchStatus.Applied"/> when the merge was written, with the files that have conflict markers left in
    /// them as <see cref="PatchApplyResult.Conflicts"/>; <see cref="PatchStatus.ConflictPending"/> with the files Git
    /// cannot merge when nothing was written.
    /// </returns>
    /// <exception cref="SyncBridgeException">Git could not be started.</exception>
    /// <exception cref="PatchApplyException">The outcome could not be written; the working tree is as it was.</exception>
    public async Task<PatchApplyResult> MergeAsync(PatchEnvelope envelope, IPatchContent content, string baseDirectory, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        var root = Path.GetFullPath(Guard.NotBlank(targetDirectory, nameof(targetDirectory)));
        var ancestor = Path.GetFullPath(Guard.NotBlank(baseDirectory, nameof(baseDirectory)));
        var scratch = Path.Combine(_appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq"), "merge-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var unmergeable = new List<ConflictFile>();
            var marked = new List<ConflictFile>();
            var manifest = new List<FileChange>();
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var index = 0; index < envelope.Manifest.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var change = envelope.Manifest[index];
                var plan = await _planner.PlanAsync(index, change, content, ancestor, root, scratch, cancellationToken);
                switch (plan.Kind)
                {
                    case MergePlanKind.TakeIncoming:
                        manifest.Add(change);
                        break;
                    case MergePlanKind.Merged:
                    case MergePlanKind.MergedWithMarkers:
                        manifest.Add(plan.Change!);
                        merged[change.Path] = plan.MergedFile!;
                        if (plan.Kind == MergePlanKind.MergedWithMarkers)
                        {
                            marked.Add(new ConflictFile(change.Path, null, change.Sha256, LocalizedText.Encode("Merge.Markers")));
                        }

                        break;
                    case MergePlanKind.CannotMerge:
                        unmergeable.Add(new ConflictFile(change.Path, null, change.Sha256, plan.Reason));
                        break;
                }
            }

            if (unmergeable.Count > 0)
            {
                return PatchApplyResult.Conflict(envelope.Id, unmergeable);
            }

            await _writer.WriteAsync(envelope, content, manifest, merged, root, cancellationToken);
            return new PatchApplyResult(envelope.Id, PatchStatus.Applied, envelope.Manifest.Count, marked, null);
        }
        finally
        {
            DeleteQuietly(scratch);
        }
    }

    /// <summary>
    /// Removes the temporary directory of a merge.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }
    }
}
