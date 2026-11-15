namespace RCynQ.UI.Views.Dialogs;

/// <summary>
/// The confirmation of an action that cannot be undone; Cancel has the focus when it opens.
/// </summary>
public partial class DangerConfirmDialog : DialogWindow
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DangerConfirmDialog"/> class.
    /// </summary>
    public DangerConfirmDialog()
    {
        InitializeComponent();
        Opened += (_, _) => CancelButton.Focus();
    }
}
