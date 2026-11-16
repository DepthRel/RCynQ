using RCynQ.Core.Enums;

namespace RCynQ.Core.State;

/// <summary>
/// A branch the Workstation working tree can be switched to: a local branch or a branch fetched from the Origin.
/// </summary>
/// <param name="Name">The branch name, without <c>refs/heads/</c> or <c>rcynq/</c>.</param>
/// <param name="Status">How the branch stands to the branches of the Origin.</param>
/// <param name="Ahead">For a pulled branch, its commits that <c>rcynq/&lt;name&gt;</c> does not have; otherwise 0.</param>
/// <param name="Behind">For a pulled branch, the commits of <c>rcynq/&lt;name&gt;</c> it does not have; otherwise 0.</param>
public sealed record GitSwitchableBranch(string Name, BranchSyncStatus Status, int Ahead, int Behind);
