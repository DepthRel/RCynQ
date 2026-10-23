namespace RCynQ.Core.Api;

/// <summary>
/// Every branch of a repository of Origin, so a Workstation sees also the branches that bring no new commits: a branch
/// created at a commit it already has, a branch moved back, a branch that was deleted.
/// </summary>
/// <param name="Head">The branch Origin has checked out, or <see langword="null"/> for a detached HEAD.</param>
/// <param name="Branches">The branches, at most <see cref="ApiLimits.MAX_GIT_BRANCHES"/>.</param>
public sealed record GitBranchListResponse(string? Head, IReadOnlyList<GitBranchTip> Branches);
