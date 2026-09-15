using CommunityToolkit.Mvvm.Input;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// A confirmation dialog.
/// </summary>
public sealed partial class ConfirmDialogViewModel : DialogViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConfirmDialogViewModel"/> class.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    public ConfirmDialogViewModel(string title, string message)
    {
        Title = title ?? string.Empty;
        Message = message ?? string.Empty;
    }

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets the message.</summary>
    public string Message { get; }

    /// <summary>
    /// Confirms the action.
    /// </summary>
    [RelayCommand]
    private void Confirm() => Close(true);

    /// <summary>
    /// Cancels the action.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
