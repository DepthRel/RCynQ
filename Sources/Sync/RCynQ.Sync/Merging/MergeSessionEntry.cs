using RCynQ.Core.Patches;
using RCynQ.Sync.Enums;

namespace RCynQ.Sync.Merging;

/// <summary>
/// What a merge session does with one file of the patch.
/// </summary>
/// <param name="Change">The incoming change.</param>
/// <param name="Kind">What the merge does with the file.</param>
/// <param name="LocalHash">The content hash of the local file when the session was prepared; the merge is valid for it only.</param>
/// <param name="MergedFile">
/// The file with the merged content: the result of Git for <see cref="MergePlanKind.Merged"/>, the file the merge tool
/// writes for <see cref="MergePlanKind.MergedWithMarkers"/>; <see langword="null"/> for the other kinds.
/// </param>
public sealed record MergeSessionEntry(FileChange Change, MergePlanKind Kind, string? LocalHash, string? MergedFile);
