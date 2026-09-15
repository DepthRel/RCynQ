namespace RCynQ.Core.State;

/// <summary>
/// The branch state of a working tree relative to the branches fetched from Dest.
/// </summary>
/// <param name="IsRepository">Whether the directory is a Git working tree.</param>
/// <param name="Branch">The current branch name, or <see langword="null"/> for a detached HEAD or non-repository.</param>
/// <param name="TrackingRef">The remote-tracking reference compared against, or <see langword="null"/> when it does not exist.</param>
/// <param name="Ahead">The number of local commits not in the tracking reference.</param>
/// <param name="Behind">The number of tracking reference commits not in the local branch.</param>
public sealed record GitBranchInfo(bool IsRepository, string? Branch, string? TrackingRef, int Ahead, int Behind);
