using CommunityToolkit.Mvvm.ComponentModel;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A row of the list of changed files on the "Синхронизация" tab: a file, or a folder when the list is shown as a tree.
/// </summary>
public abstract partial class ChangedRowViewModel : ObservableObject
{
    /// <summary>The text of the row: the path of a file in the flat list, the name of a file or folder in the tree.</summary>
    [ObservableProperty]
    private string _label = string.Empty;

    /// <summary>The indentation of the row in device-independent pixels, which shows its depth in the tree.</summary>
    [ObservableProperty]
    private double _indentWidth;

    /// <summary>Whether the row shows a check mark, that is the user picks the files to send.</summary>
    [ObservableProperty]
    private bool _isSelectable;
}
