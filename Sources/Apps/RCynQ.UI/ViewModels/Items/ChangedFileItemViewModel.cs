using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.State;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the list of changed files on the "Синхронизация" tab.
/// </summary>
public sealed partial class ChangedFileItemViewModel : ObservableObject
{
    /// <summary>The action called when the row is checked or unchecked.</summary>
    private readonly Action? _selectionChanged;

    /// <summary>Whether the file is chosen to be sent.</summary>
    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Whether the row shows a check mark, that is the user picks the files to send.</summary>
    [ObservableProperty]
    private bool _isSelectable;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedFileItemViewModel"/> class.
    /// </summary>
    /// <param name="file">The changed file.</param>
    /// <param name="selectionChanged">The action called when the row is checked or unchecked, or <see langword="null"/>.</param>
    public ChangedFileItemViewModel(ChangedFile file, Action? selectionChanged = null)
    {
        File = file ?? throw new ArgumentNullException(nameof(file));
        _selectionChanged = selectionChanged;
    }

    /// <summary>Gets the changed file.</summary>
    public ChangedFile File { get; }

    /// <summary>Gets the relative path of the file.</summary>
    public string Path => File.Path;

    /// <summary>Gets the one-letter marker of the change.</summary>
    public string KindText => DisplayText.Change(File.Kind);

    /// <summary>Gets a value indicating whether the neighbour already has this version of the file.</summary>
    public bool AlreadySent => File.AlreadySent;

    /// <summary>
    /// Tells the owner that the selection changed.
    /// </summary>
    /// <param name="value">The new value.</param>
    partial void OnIsSelectedChanged(bool value) => _selectionChanged?.Invoke();
}
