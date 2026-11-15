namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// What can be done with one changed file of the list: its changes, the choice of those that go, and taking them back.
/// </summary>
public interface IChangedFileActions
{
    /// <summary>
    /// Gets a value indicating whether the changes of a file can be taken back here: on a Workstation whose working tree
    /// is a Git repository.
    /// </summary>
    bool CanRevertChanges { get; }

    /// <summary>
    /// Shows the diff of the file.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task that completes when the diff is shown (or closed, in the window of RCynQ).</returns>
    Task ShowDiffAsync(ChangedFileItemViewModel file);

    /// <summary>
    /// Lets the user choose the changes of the file that go.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task that completes when the choice is made or given up.</returns>
    Task ChooseChangesAsync(ChangedFileItemViewModel file);

    /// <summary>
    /// Chooses every change of the file again, so the whole file goes.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task that completes when the choice is gone.</returns>
    Task ChooseAllChangesAsync(ChangedFileItemViewModel file);

    /// <summary>
    /// Takes back the changes of the file that are not staged, once the user confirms it.
    /// </summary>
    /// <param name="file">The file.</param>
    /// <returns>A task that completes when the changes are taken back or the user gave it up.</returns>
    Task RevertChangesAsync(ChangedFileItemViewModel file);
}
