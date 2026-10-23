using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Api;

/// <summary>
/// The answer of <c>GET /commits</c>: the commits of the current branch of a Workstation that the Origin does not have yet.
/// </summary>
/// <param name="State">Whether the commits can be listed and sent, and why not.</param>
/// <param name="Branch">The checked out branch, or <see langword="null"/>.</param>
/// <param name="OriginCommit">The commit the Origin has on top of the branch, or <see langword="null"/>.</param>
/// <param name="HeadCommit">The commit checked out, or <see langword="null"/>.</param>
/// <param name="Commits">The unsent commits on the first-parent chain, oldest first.</param>
/// <param name="WaitingCommits">The hashes of the unsent commits that a delivery is waiting with on the Origin.</param>
/// <param name="Problem">Why no commits can be listed or sent, worded for the user; <see langword="null"/> when they can.</param>
public sealed record UnsentCommitsResponse(
    UnsentCommitsState State,
    string? Branch,
    string? OriginCommit,
    string? HeadCommit,
    IReadOnlyList<CommitInfo> Commits,
    IReadOnlyList<string> WaitingCommits,
    string? Problem);
