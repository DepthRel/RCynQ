using CommunityToolkit.Mvvm.ComponentModel;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// The head of one change of a file: its check mark and where it is.
/// </summary>
public sealed partial class DiffBlockRowViewModel : DiffRowViewModel
{
    /// <summary>The action called when the change is checked or unchecked.</summary>
    private readonly Action _selectionChanged;

    /// <summary>Whether the change goes to the neighbour.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiffBlockRowViewModel"/> class.
    /// </summary>
    /// <param name="index">The number of the change in the file, from 0.</param>
    /// <param name="text">The head text.</param>
    /// <param name="canChoose">Whether the change can be checked.</param>
    /// <param name="isSelected">Whether the change goes now.</param>
    /// <param name="selectionChanged">The action called when the change is checked or unchecked.</param>
    public DiffBlockRowViewModel(int index, string text, bool canChoose, bool isSelected, Action selectionChanged)
    {
        Index = index;
        Text = text ?? string.Empty;
        CanChoose = canChoose;
        _isSelected = isSelected;
        _selectionChanged = selectionChanged ?? throw new ArgumentNullException(nameof(selectionChanged));
    }

    /// <summary>Gets the number of the change in the file, from 0.</summary>
    public int Index { get; }

    /// <summary>Gets the head text.</summary>
    public string Text { get; }

    /// <summary>Gets a value indicating whether the change can be checked.</summary>
    public bool CanChoose { get; }

    /// <summary>
    /// Tells the owner that the check mark changed.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsSelectedChanged(bool value) => _selectionChanged();
}
