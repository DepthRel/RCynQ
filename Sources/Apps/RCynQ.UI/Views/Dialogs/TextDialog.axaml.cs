using Avalonia.Interactivity;

namespace RCynQ.UI.Views.Dialogs;

/// <summary>
/// A read-only text dialog.
/// </summary>
public partial class TextDialog : DialogWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextDialog"/> class.
    /// </summary>
    public TextDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Closes the dialog.
    /// </summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The event arguments.</param>
    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close(false);
}
