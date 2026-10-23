namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// One choice of how far a rollback of commits goes.
/// </summary>
/// <param name="Count">The number of the newest commits that are rolled back.</param>
/// <param name="Target">The commit that becomes <c>HEAD</c>.</param>
/// <param name="Label">The text of the choice.</param>
public sealed record RollbackTargetOption(int Count, string Target, string Label);
