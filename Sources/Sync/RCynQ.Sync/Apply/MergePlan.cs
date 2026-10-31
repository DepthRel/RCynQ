using RCynQ.Core.Patches;
using RCynQ.Sync.Enums;

namespace RCynQ.Sync.Apply;

/// <summary>
/// The outcome of the three-way merge of one file, worked out in a temporary directory without touching the working tree.
/// </summary>
/// <param name="Kind">What the merge does with the file.</param>
/// <param name="LocalHash">The content hash of the local file when it was planned (<see cref="State.FileState.ContentHashAsync"/>).</param>
/// <param name="Change">The change that writes the merged file, for <see cref="MergePlanKind.Merged"/> and <see cref="MergePlanKind.MergedWithMarkers"/>.</param>
/// <param name="MergedFile">The temporary file with the merged content, for the same kinds.</param>
/// <param name="LocalFile">The temporary copy of the local file the merge started from, for the same kinds.</param>
/// <param name="BaseFile">The temporary copy of the common ancestor, in the line endings of the local file, for the same kinds.</param>
/// <param name="IncomingFile">The temporary copy of the incoming file, in the line endings of the local file, for the same kinds.</param>
/// <param name="Reason">Why the file cannot be merged, as an encoded message, for <see cref="MergePlanKind.CannotMerge"/>.</param>
public sealed record MergePlan(
    MergePlanKind Kind,
    string? LocalHash,
    FileChange? Change = null,
    string? MergedFile = null,
    string? LocalFile = null,
    string? BaseFile = null,
    string? IncomingFile = null,
    string? Reason = null);
