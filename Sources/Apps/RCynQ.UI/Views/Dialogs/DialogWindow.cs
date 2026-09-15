using Avalonia.Controls;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Views.Dialogs;

/// <summary>
/// Base window of modal dialogs: closes itself when its <see cref="DialogViewModelBase"/> requests it.
/// </summary>
public class DialogWindow : Window
{
    /// <summary>The current dialog view model.</summary>
    private DialogViewModelBase? _dialog;

    /// <inheritdoc />
    protected override Type StyleKeyOverride => typeof(Window);

    /// <inheritdoc />
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_dialog is not null)
        {
            _dialog.CloseRequested -= OnCloseRequested;
        }

        _dialog = DataContext as DialogViewModelBase;
        if (_dialog is not null)
        {
            _dialog.CloseRequested += OnCloseRequested;
        }
    }

    /// <summary>
    /// Closes the window with the dialog result.
    /// </summary>
    /// <param name="sender">The view model.</param>
    /// <param name="confirmed">Whether the user confirmed.</param>
    private void OnCloseRequested(object? sender, bool confirmed) => Close(confirmed);
}
