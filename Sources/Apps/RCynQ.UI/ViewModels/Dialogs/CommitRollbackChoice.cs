using RCynQ.Core.Enums;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// What the user chose in the rollback dialog.
/// </summary>
/// <param name="Target">The commit that becomes <c>HEAD</c>.</param>
/// <param name="Mode">What happens to the changes of the dropped commits.</param>
public sealed record CommitRollbackChoice(string Target, CommitResetMode Mode);
