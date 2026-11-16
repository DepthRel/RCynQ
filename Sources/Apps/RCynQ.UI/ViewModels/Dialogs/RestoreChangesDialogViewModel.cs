using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog shown after a switch that saved the uncommitted changes to a patch file: brings them back from that file
/// into the working tree of the new branch, or leaves them in the file for later. The file is the one just written, so
/// nothing has to be chosen again.
/// </summary>
public sealed partial class RestoreChangesDialogViewModel : DialogViewModelBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RestoreChangesDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="branch">The branch the working tree is on now.</param>
    /// <param name="patchPath">The patch file the changes were saved to.</param>
    public RestoreChangesDialogViewModel(ILocalizer localizer, string branch, string patchPath)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentException.ThrowIfNullOrWhiteSpace(patchPath);

        Branch = branch;
        PatchPath = patchPath;
        HintText = localizer.Format("BranchSwitch.Restore.Hint", branch);
    }

    /// <summary>Gets the branch the working tree is on now.</summary>
    public string Branch { get; }

    /// <summary>Gets the patch file the changes were saved to.</summary>
    public string PatchPath { get; }

    /// <summary>Gets the explanation under the title.</summary>
    public string HintText { get; }

    /// <summary>
    /// Brings the changes back from the patch file.
    /// </summary>
    [RelayCommand]
    private void Restore() => Close(true);

    /// <summary>
    /// Leaves the changes in the patch file.
    /// </summary>
    [RelayCommand]
    private void Later() => Close(false);
}
