using RCynQ.Core.State;

namespace RCynQ.Core.Api;

/// <summary>
/// The branches the Workstation working tree can be switched to.
/// </summary>
/// <param name="Current">The current branch, or <see langword="null"/> for a detached HEAD or no repository.</param>
/// <param name="OriginHead">
/// The branch the Origin had checked out at the last Fetch, or <see langword="null"/> when it is not known.
/// </param>
/// <param name="Branches">The branches, sorted by name.</param>
public sealed record GitSwitchBranchesResponse(string? Current, string? OriginHead, IReadOnlyList<GitSwitchableBranch> Branches);
