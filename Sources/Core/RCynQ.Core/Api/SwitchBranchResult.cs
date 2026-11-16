using RCynQ.Core.Enums;
using RCynQ.Core.State;

namespace RCynQ.Core.Api;

/// <summary>
/// How a switch of the Workstation to another branch ended.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">What happened, in the language of the user.</param>
/// <param name="Files">
/// With <see cref="BranchSwitchOutcome.ChangesInTheWay"/>, the changed files; when Git refused to carry changes, the files
/// in the way; otherwise empty.
/// </param>
/// <param name="Branch">The branch state after the request, or <see langword="null"/> when it could not be read.</param>
public sealed record SwitchBranchResult(BranchSwitchOutcome Outcome, string Message, IReadOnlyList<string> Files, GitBranchInfo? Branch);
