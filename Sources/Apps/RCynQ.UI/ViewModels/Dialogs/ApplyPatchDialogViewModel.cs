using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.ViewModels.Items;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog that chooses the files of a patch to apply, as a flat list or as a tree of folders; every file starts checked.
/// </summary>
public sealed partial class ApplyPatchDialogViewModel : DialogViewModelBase, IChangedFolderOwner
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The store of the interface settings, or <see langword="null"/> when the layout is not kept.</summary>
    private readonly IUiSettingsStore? _uiSettings;

    /// <summary>The folders the user closed in the tree, by path.</summary>
    private readonly HashSet<string> _collapsedFolders = new(StringComparer.Ordinal);

    /// <summary>Whether many files are checked at once, so what depends on the check marks is updated once at the end.</summary>
    private bool _isBatchSelecting;

    /// <summary>Whether the files are shown as a tree of folders; otherwise as a flat list of paths.</summary>
    [ObservableProperty]
    private bool _isTreeView;

    /// <summary>
    /// Initializes a new instance of the <see cref="ApplyPatchDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="fileName">The name of the patch file.</param>
    /// <param name="files">The files of the patch.</param>
    /// <param name="uiSettings">The store that keeps how the files are laid out, or <see langword="null"/>.</param>
    public ApplyPatchDialogViewModel(ILocalizer localizer, string fileName, IReadOnlyList<ChangedFile> files, IUiSettingsStore? uiSettings = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(files);

        _uiSettings = uiSettings;
        _isBatchSelecting = true;
        Files = files.Select(file => new ChangedFileItemViewModel(file, OnSelectionChanged) { IsSelectable = true, IsSelected = true }).ToArray();
        _isBatchSelecting = false;
        Description = localizer.Format("ApplyPatch.Description", fileName);
        _isTreeView = ChangedFilesLayoutSetting.IsTree(uiSettings);
        RebuildRows();
    }

    /// <summary>Gets the text under the title.</summary>
    public string Description { get; }

    /// <summary>Gets the files of the patch.</summary>
    public IReadOnlyList<ChangedFileItemViewModel> Files { get; }

    /// <summary>Gets the rows on screen: the files as a flat list, or as a tree with folder rows.</summary>
    public ObservableCollection<ChangedRowViewModel> Rows { get; } = new();

    /// <summary>Gets the number of checked files.</summary>
    public int SelectedCount => Files.Count(file => file.IsSelected);

    /// <summary>Gets the number of checked files for the title of the list.</summary>
    public string CountText => $"({_localizer.Format("Sync.SelectedOf", SelectedCount, Files.Count)})";

    /// <summary>Gets a value indicating whether the patch can be applied: at least one file has to be checked.</summary>
    public bool CanApply => SelectedCount > 0;

    /// <summary>Gets the checked files, or <see langword="null"/> when the whole patch is applied.</summary>
    public IReadOnlyList<string>? ChosenPaths => SelectedCount == Files.Count ? null : Files.Where(file => file.IsSelected).Select(file => file.Path).ToArray();

    /// <inheritdoc />
    public bool IsFolderExpanded(string path) => !_collapsedFolders.Contains(path);

    /// <inheritdoc />
    public void ToggleFolder(ChangedFolderItemViewModel folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (!_collapsedFolders.Remove(folder.Path))
        {
            _collapsedFolders.Add(folder.Path);
        }

        RebuildRows();
    }

    /// <inheritdoc />
    public void SetSelected(IReadOnlyList<ChangedFileItemViewModel> files, bool selected)
    {
        ArgumentNullException.ThrowIfNull(files);

        _isBatchSelecting = true;
        try
        {
            foreach (var file in files)
            {
                file.IsSelected = selected;
            }
        }
        finally
        {
            _isBatchSelecting = false;
        }

        OnSelectionChanged();
    }

    /// <summary>
    /// Shows the files as a flat list of paths.
    /// </summary>
    [RelayCommand]
    private void ShowFlat() => IsTreeView = false;

    /// <summary>
    /// Shows the files as a tree of folders.
    /// </summary>
    [RelayCommand]
    private void ShowTree() => IsTreeView = true;

    /// <summary>
    /// Checks every file.
    /// </summary>
    [RelayCommand]
    private void SelectAll() => SetSelected(Files, true);

    /// <summary>
    /// Unchecks every file.
    /// </summary>
    [RelayCommand]
    private void ClearSelection() => SetSelected(Files, false);

    /// <summary>
    /// Confirms the choice of files.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => Close(true);

    /// <summary>
    /// Closes the dialog without applying anything.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);

    /// <summary>
    /// Lays the rows out in the chosen way and keeps the choice for the next start.
    /// </summary>
    /// <param name="value">Whether the files are shown as a tree.</param>
    partial void OnIsTreeViewChanged(bool value)
    {
        RebuildRows();
        ChangedFilesLayoutSetting.Save(_uiSettings, value);
    }

    /// <summary>
    /// Lays the rows out again from the files, the chosen layout and the closed folders.
    /// </summary>
    private void RebuildRows()
    {
        var rows = IsTreeView ? ChangedFileRows.Tree(Files, this) : ChangedFileRows.Flat(Files);

        Rows.Clear();
        foreach (var row in rows)
        {
            row.IsSelectable = true;
            Rows.Add(row);
        }
    }

    /// <summary>
    /// Updates what depends on the check marks.
    /// </summary>
    private void OnSelectionChanged()
    {
        if (_isBatchSelecting)
        {
            return;
        }

        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();
        foreach (var folder in Rows.OfType<ChangedFolderItemViewModel>())
        {
            folder.RefreshSelection();
        }
    }
}
