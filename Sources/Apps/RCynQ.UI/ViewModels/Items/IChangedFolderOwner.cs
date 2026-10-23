namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// The list that owns the folder rows of the tree of changed files: it remembers which folders are open and checks files.
/// </summary>
public interface IChangedFolderOwner
{
    /// <summary>
    /// Tells whether a folder is shown open, with its contents below it.
    /// </summary>
    /// <param name="path">The path of the folder relative to the working tree.</param>
    /// <returns><see langword="true"/> when the folder is open.</returns>
    bool IsFolderExpanded(string path);

    /// <summary>
    /// Opens a closed folder or closes an open one.
    /// </summary>
    /// <param name="folder">The folder row.</param>
    void ToggleFolder(ChangedFolderItemViewModel folder);

    /// <summary>
    /// Checks or unchecks files at once.
    /// </summary>
    /// <param name="files">The files.</param>
    /// <param name="selected">Whether the files are checked.</param>
    void SetSelected(IReadOnlyList<ChangedFileItemViewModel> files, bool selected);
}
