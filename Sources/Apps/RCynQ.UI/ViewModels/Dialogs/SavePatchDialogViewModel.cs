using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog that asks how to save the changes to a patch file. It stays open under the file picker, so giving the
/// picker up leads back here, and closes as soon as the file is chosen: the page does the work and shows its progress.
/// </summary>
public sealed partial class SavePatchDialogViewModel : DialogViewModelBase
{
    /// <summary>Asks where to save; gives <see langword="null"/> when the user gave up.</summary>
    private readonly Func<Task<string?>> _pickFile;

    /// <summary>Whether the changes stay in the working branch; otherwise they leave it together with the patch.</summary>
    [ObservableProperty]
    private bool _keepChanges = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SavePatchDialogViewModel"/> class.
    /// </summary>
    /// <param name="pickFile">Asks where to save; gives <see langword="null"/> when the user gave up.</param>
    public SavePatchDialogViewModel(Func<Task<string?>> pickFile)
    {
        _pickFile = pickFile ?? throw new ArgumentNullException(nameof(pickFile));
    }

    /// <summary>Gets the file the patch goes to, once the user has chosen it.</summary>
    public string? PatchPath { get; private set; }

    /// <summary>
    /// Asks where to save; the dialog closes unless the user gave up on the picker.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task SaveAsync()
    {
        PatchPath = await _pickFile();
        if (PatchPath is not null)
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
