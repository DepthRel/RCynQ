using Avalonia.Controls;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Views.Dialogs;

/// <summary>
/// The window of a merge in a merge tool. It starts the merge once it is open, and closing it gives the merge up instead
/// of leaving the merge running without a window.
/// </summary>
public partial class MergeProgressDialog : DialogWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MergeProgressDialog"/> class.
    /// </summary>
    public MergeProgressDialog()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (DataContext is MergeProgressDialogViewModel dialog)
            {
                _ = dialog.RunAsync();
            }
        };
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The window stays until the merge has ended; the merge closes it then, after giving up when asked to.
        if (DataContext is MergeProgressDialogViewModel { IsFinished: false } dialog)
        {
            e.Cancel = true;
            dialog.CancelCommand.Execute(null);
        }

        base.OnClosing(e);
    }
}
