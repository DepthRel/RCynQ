using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog shown when the working tree has uncommitted changes and the user switches to another branch: carry the
/// changes over, save them to a patch file first, or stay. There is deliberately no choice that throws them away.
/// </summary>
/// <remarks>
/// Saving asks for the file while the dialog is still open, as the dialog of saving a patch does: the picker belongs to
/// this window, giving the picker up leads back here, and the dialog closes only once the file is chosen.
/// </remarks>
public sealed partial class SwitchBranchDialogViewModel : DialogViewModelBase
{
    /// <summary>Asks where to save the changes; gives <see langword="null"/> when the user gave up.</summary>
    private readonly Func<Task<string?>> _pickFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwitchBranchDialogViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="branch">The branch the user switches to.</param>
    /// <param name="files">The files with uncommitted changes.</param>
    /// <param name="pickFile">Asks where to save the changes; gives <see langword="null"/> when the user gave up.</param>
    public SwitchBranchDialogViewModel(ILocalizer localizer, string branch, IReadOnlyList<string> files, Func<Task<string?>> pickFile)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentException.ThrowIfNullOrWhiteSpace(branch);
        ArgumentNullException.ThrowIfNull(files);

        _pickFile = pickFile ?? throw new ArgumentNullException(nameof(pickFile));
        Branch = branch;
        Files = files;
        HintText = localizer.Format("BranchSwitch.Dialog.Hint", branch, files.Count);
        CarryHintText = localizer.Format("BranchSwitch.Dialog.CarryHint", branch);
    }

    /// <summary>Gets the branch the user switches to.</summary>
    public string Branch { get; }

    /// <summary>Gets the files with uncommitted changes.</summary>
    public IReadOnlyList<string> Files { get; }

    /// <summary>Gets the explanation above the list of files.</summary>
    public string HintText { get; }

    /// <summary>Gets the explanation of carrying the changes over.</summary>
    public string CarryHintText { get; }

    /// <summary>Gets what the user chose, or <see langword="null"/> when the dialog was cancelled.</summary>
    public BranchSwitchMode? Result { get; private set; }

    /// <summary>Gets the file the changes are saved to, once the user chose <see cref="BranchSwitchMode.SavePatch"/>.</summary>
    public string? PatchPath { get; private set; }

    /// <summary>
    /// Chooses to carry the changes over to the new branch.
    /// </summary>
    [RelayCommand]
    private void Carry() => Choose(BranchSwitchMode.CarryChanges);

    /// <summary>
    /// Asks where to save the changes before the switch takes them out; the dialog stays open when the user gives the
    /// picker up.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SavePatchAsync()
    {
        var file = await _pickFile();
        if (file is null)
        {
            return;
        }

        PatchPath = file;
        Choose(BranchSwitchMode.SavePatch);
    }

    /// <summary>
    /// Closes the dialog without switching.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        PatchPath = null;
        Close(false);
    }

    /// <summary>
    /// Records the choice and closes the dialog.
    /// </summary>
    /// <param name="mode">The choice.</param>
    private void Choose(BranchSwitchMode mode)
    {
        Result = mode;
        Close(true);
    }
}
