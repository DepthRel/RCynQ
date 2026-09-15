namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// A read-only text dialog such as a patch diff.
/// </summary>
public sealed class TextDialogViewModel : DialogViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TextDialogViewModel"/> class.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="text">The text.</param>
    public TextDialogViewModel(string title, string text)
    {
        Title = title ?? string.Empty;
        Text = text ?? string.Empty;
    }

    /// <summary>Gets the title.</summary>
    public string Title { get; }

    /// <summary>Gets the text.</summary>
    public string Text { get; }
}
