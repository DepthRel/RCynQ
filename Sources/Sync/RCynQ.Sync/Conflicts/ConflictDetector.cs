using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Conflicts;

/// <summary>
/// Detects files whose local content changed since the state the sender built the patch from and differs from the incoming content.
/// </summary>
/// <remarks>
/// For every changed file: equal local and incoming content is no conflict; local content equal to the base the sender reported
/// (<see cref="FileChange.BaseSha256"/>, "no file" for an added one) can be overwritten; anything else is a conflict. A sender of an
/// older version reports no base for modified and deleted files; their local content is then compared with the local sync base.
/// The base comparison ignores whether lines end in CRLF or LF (see <see cref="FileState.ContentHashAsync"/>).
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
    /// <param name="syncBase">The sync base, used for changes whose sender reported no base.</param>
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

            // The base is compared by content, not by line endings: the same file checked out on two nodes may differ only in CRLF.
            var localContent = await FileState.ContentHashAsync(_paths.ResolveWithinRoot(workdir, change.Path), cancellationToken);
            var localBase = change.HasKnownBase
                ? change.BaseSha256
                : await FileState.ContentHashAsync(_paths.ResolveWithinRoot(syncBase, change.Path), cancellationToken);
            if (localContent == localBase)
            {
                continue;
            }

            var detail = local switch
            {
                null => LocalizedText.Encode("Conflict.DeletedLocally"),
                FileState.DIRECTORY_MARKER => LocalizedText.Encode("Conflict.DirectoryLocally"),
                _ when localBase is null => LocalizedText.Encode("Conflict.CreatedLocally"),
                _ => LocalizedText.Encode("Conflict.ModifiedLocally")
            };
            conflicts.Add(new ConflictFile(change.Path, local == FileState.DIRECTORY_MARKER ? null : local, incoming, detail));
        }

        return conflicts;
    }
}
