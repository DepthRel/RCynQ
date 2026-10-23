using CommunityToolkit.Mvvm.Input;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A folder row of the tree of changed files on the "Синхронизация" tab.
/// </summary>
public sealed partial class ChangedFolderItemViewModel : ChangedRowViewModel
{
    /// <summary>The list that owns the row.</summary>
    private readonly IChangedFolderOwner _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangedFolderItemViewModel"/> class.
    /// </summary>
    /// <param name="owner">The list that owns the row.</param>
    /// <param name="path">The path of the folder relative to the working tree.</param>
    /// <param name="files">The shown files anywhere below the folder.</param>
    public ChangedFolderItemViewModel(IChangedFolderOwner owner, string path, IReadOnlyList<ChangedFileItemViewModel> files)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Files = files ?? throw new ArgumentNullException(nameof(files));
        IsExpanded = owner.IsFolderExpanded(path);
    }

    /// <summary>Gets the path of the folder relative to the working tree.</summary>
    public string Path { get; }

    /// <summary>Gets the shown files anywhere below the folder.</summary>
    public IReadOnlyList<ChangedFileItemViewModel> Files { get; }

    /// <summary>Gets the number of the shown files below the folder.</summary>
    public int FileCount => Files.Count;

    /// <summary>Gets a value indicating whether the folder is open, with its contents below it.</summary>
    public bool IsExpanded { get; }

    /// <summary>
    /// Gets or sets the check mark of the folder: set when every file below is checked, cleared when none is, and
    /// undetermined (<see langword="null"/>) in between. Checking the folder checks every file below it.
    /// </summary>
    public bool? IsChecked
    {
        get
        {
            var selected = Files.Count(file => file.IsSelected);
            if (selected == 0)
            {
                return false;
            }

            return selected == Files.Count ? true : null;
        }

        set
        {
            _owner.SetSelected(Files, value == true);
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Tells the bindings that the files below were checked or unchecked.
    /// </summary>
    public void RefreshSelection() => OnPropertyChanged(nameof(IsChecked));

    /// <summary>
    /// Opens or closes the folder.
    /// </summary>
    [RelayCommand]
    private void Toggle() => _owner.ToggleFolder(this);
}
