using RCynQ.Core.Api;

namespace RCynQ.Sync.Merging;

/// <summary>
/// A conflicting patch prepared for a merge tool: what happens to each of its files, and the files the user merges.
/// </summary>
/// <param name="Id">The identifier of the session.</param>
/// <param name="PatchId">The patch.</param>
/// <param name="Directory">The directory of the files of the session.</param>
/// <param name="CreatedAt">When the session was prepared.</param>
/// <param name="Entries">What happens to each file of the patch, in the order of the patch.</param>
/// <param name="Files">The files the user merges in the tool.</param>
/// <param name="AutoMergedCount">How many files changed on both sides Git merged on its own.</param>
public sealed record MergeSession(
    Guid Id,
    Guid PatchId,
    string Directory,
    DateTimeOffset CreatedAt,
    IReadOnlyList<MergeSessionEntry> Entries,
    IReadOnlyList<MergeSessionFile> Files,
    int AutoMergedCount);
