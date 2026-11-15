using CommunityToolkit.Mvvm.Input;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The confirmation of an action that cannot be undone: the warning is shown in red, and the safe answer, Cancel, is the
/// one the dialog offers first.
/// </summary>
public sealed partial class DangerConfirmDialogViewModel : DialogViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DangerConfirmDialogViewModel"/> class.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">What the action does.</param>
    /// <param name="warning">The warning shown in red.</param>
    /// <param name="confirmText">The text of the button that does the action.</param>
    public DangerConfirmDialogViewModel(string title, string message, string warning, string confirmText)
    {
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
        Warning = warning ?? string.Empty;
        ConfirmText = confirmText ?? string.Empty;
    }

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets what the action does.</summary>
    public string Message { get; }

    /// <summary>Gets the warning shown in red.</summary>
    public string Warning { get; }

    /// <summary>Gets the text of the button that does the action.</summary>
    public string ConfirmText { get; }

    /// <summary>
    /// Does the action.
    /// </summary>
    [RelayCommand]
    private void Confirm() => Close(true);

    /// <summary>
    /// Gives the action up.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
