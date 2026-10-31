using RCynQ.Core.Patches;

namespace RCynQ.Core.Api;

/// <summary>
/// Answer of <c>POST /patches/{id}/merge-sessions</c>: the files of a patch the user merges in a merge tool.
/// </summary>
/// <param name="SessionId">The session, or <see langword="null"/> when some file cannot be merged and no session was opened.</param>
/// <param name="PatchId">The patch.</param>
/// <param name="AutoMergedCount">How many files changed on both sides Git merged on its own; they are written with the rest.</param>
/// <param name="Files">The files whose changes collide, to open in the tool one by one.</param>
/// <param name="Unmergeable">The files no merge tool can merge (binary, a link, deleted on one side); empty when a session was opened.</param>
public sealed record MergeSessionResponse(
    Guid? SessionId,
    Guid PatchId,
    int AutoMergedCount,
    IReadOnlyList<MergeSessionFile> Files,
    IReadOnlyList<ConflictFile> Unmergeable);
