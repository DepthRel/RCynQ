using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog that saves the changes to a patch file. It stays open under the file picker, so giving the picker up leads
/// back here.
/// </summary>
public sealed partial class SavePatchDialogViewModel : DialogViewModelBase
{
    /// <summary>Asks where to save and saves; tells whether the dialog is done, with the choice about the changes as its argument.</summary>
    private readonly Func<bool, Task<bool>> _save;

    /// <summary>Whether the changes stay in the working branch; otherwise they leave it together with the patch.</summary>
    [ObservableProperty]
    private bool _keepChanges = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SavePatchDialogViewModel"/> class.
    /// </summary>
    /// <param name="save">Asks where to save and saves; gets the choice about the changes and returns <see langword="false"/> when the user gave up.</param>
    public SavePatchDialogViewModel(Func<bool, Task<bool>> save)
    {
        _save = save ?? throw new ArgumentNullException(nameof(save));
    }

    /// <summary>
    /// Asks where to save and saves; the dialog closes unless the user gave up on the picker.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (await _save(KeepChanges))
        {
            Close(true);
        }
    }

    /// <summary>
    /// Closes the dialog without saving anything.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
