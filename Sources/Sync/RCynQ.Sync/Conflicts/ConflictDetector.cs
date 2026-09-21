using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Sync.State;
using RCynQ.Localization;

namespace RCynQ.Sync.Conflicts;

/// <summary>
/// Detects files whose local content diverged from the sync base and differs from the incoming content.
/// </summary>
/// <remarks>
/// For every changed file: equal local and incoming content is no conflict; unchanged local content
/// (equal to the sync base) can be overwritten; anything else is a conflict.
/// </remarks>
public sealed class ConflictDetector
{
    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictDetector"/> class.
    /// </summary>
    /// <param name="paths">The path utility.</param>
    public ConflictDetector(IPathUtility paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// Detects conflicts for a manifest.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="workdir">The working tree.</param>
    /// <param name="syncBase">The sync base.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The conflicts; empty when the patch can be applied safely.</returns>
    public async Task<IReadOnlyList<ConflictFile>> DetectAsync(IReadOnlyList<FileChange> manifest, string workdir, string syncBase, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var conflicts = new List<ConflictFile>();
        foreach (var change in manifest)
        {
            var local = await FileState.HashAsync(_paths.ResolveWithinRoot(workdir, change.Path), cancellationToken);
            var incoming = change.Kind == ChangeKind.Deleted ? null : change.Sha256;
            if (local == incoming)
            {
                continue;
            }

            var localBase = await FileState.HashAsync(_paths.ResolveWithinRoot(syncBase, change.Path), cancellationToken);
            if (local == localBase)
            {
                continue;
            }

            var detail = local switch
            {
                null => LocalizedText.Encode("Conflict.DeletedLocally"),
                FileState.DirectoryMarker => LocalizedText.Encode("Conflict.DirectoryLocally"),
                _ when localBase is null => LocalizedText.Encode("Conflict.CreatedLocally"),
                _ => LocalizedText.Encode("Conflict.ModifiedLocally")
            };
            conflicts.Add(new ConflictFile(change.Path, local == FileState.DirectoryMarker ? null : local, incoming, detail));
        }

        return conflicts;
    }
}
