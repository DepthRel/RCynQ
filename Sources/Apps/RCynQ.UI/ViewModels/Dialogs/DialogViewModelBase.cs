using CommunityToolkit.Mvvm.ComponentModel;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// Base class of dialog view models.
/// </summary>
public abstract class DialogViewModelBase : ObservableObject
{
    /// <summary>
    /// Raised when the dialog should close; the argument tells whether the user confirmed.
    /// </summary>
    public event EventHandler<bool>? CloseRequested;

    /// <summary>
    /// Gets a value indicating whether the dialog was confirmed.
    /// </summary>
    public bool Confirmed { get; private set; }

    /// <summary>
    /// Closes the dialog.
    /// </summary>
    /// <param name="confirmed">Whether the user confirmed.</param>
    protected void Close(bool confirmed)
    {
        Confirmed = confirmed;
        CloseRequested?.Invoke(this, confirmed);
    }
}
