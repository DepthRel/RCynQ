using RCynQ.Core.Enums;
using RCynQ.MergeTools;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// What the user chose in the conflict dialog: a policy the service applies, or a merge tool to merge the files in.
/// </summary>
/// <param name="Policy">The policy, or <see langword="null"/> when a merge tool was chosen.</param>
/// <param name="Tool">The merge tool, or <see langword="null"/> when a policy was chosen.</param>
public sealed record ConflictChoice(ConflictPolicy? Policy, MergeTool? Tool);
