using RCynQ.UI.Services.Merging;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Wizard;

namespace RCynQ.UI.Services.Dialogs;

/// <summary>
/// Dialogs and file pickers used by view models.
/// </summary>
public interface IDialogService
{
    /// <summary>
    /// Shows the conflict dialog.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns>The chosen policy or merge tool, or <see langword="null"/> when cancelled.</returns>
    Task<ConflictChoice?> ShowConflictDialogAsync(ConflictDialogViewModel dialog);

    /// <summary>
    /// Shows the window of a merge in a merge tool and runs the merge while it is open.
    /// </summary>
    /// <param name="dialog">The dialog view model, which runs the merge and closes when it ends.</param>
    /// <returns>How the merge ended, or <see langword="null"/> when there is no window to show it in.</returns>
    Task<ExternalMergeOutcome?> ShowMergeProgressAsync(MergeProgressDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that rolls back unsent commits.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns>The choice, or <see langword="null"/> when cancelled.</returns>
    Task<CommitRollbackChoice?> ShowRollbackCommitsDialogAsync(RollbackCommitsDialogViewModel dialog);

    /// <summary>
    /// Shows the export dialog that asks for the advertised SSH address.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ShowExportDialogAsync(ExportDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that saves the changes to a patch file.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the patch was saved.</returns>
    Task<bool> ShowSavePatchDialogAsync(SavePatchDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that chooses the files of a patch to apply.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ShowApplyPatchDialogAsync(ApplyPatchDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that adds a chain node.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ShowAddNodeDialogAsync(AddNodeDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that asks what to do with an imported node of a role that is known already.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed a decision.</returns>
    Task<bool> ShowImportDecisionDialogAsync(ImportDecisionDialogViewModel dialog);

    /// <summary>
    /// Shows the connect wizard.
    /// </summary>
    /// <param name="wizard">The wizard view model.</param>
    /// <returns><see langword="true"/> when the wizard ended with a link set up.</returns>
    Task<bool> ShowConnectWizardAsync(ConnectWizardViewModel wizard);

    /// <summary>
    /// Shows read-only text such as a diff.
    /// </summary>
    /// <param name="title">The window title.</param>
    /// <param name="text">The text.</param>
    /// <returns>A task that completes when the dialog is closed.</returns>
    Task ShowTextAsync(string title, string text);

    /// <summary>
    /// Asks the user to confirm an action.
    /// </summary>
    /// <param name="title">The title.</param>
    /// <param name="message">The message.</param>
    /// <returns><see langword="true"/> when confirmed.</returns>
    Task<bool> ConfirmAsync(string title, string message);

    /// <summary>
    /// Picks a folder.
    /// </summary>
    /// <param name="title">The picker title.</param>
    /// <returns>The folder path, or <see langword="null"/>.</returns>
    Task<string?> PickFolderAsync(string title);

    /// <summary>
    /// Picks a file to open.
    /// </summary>
    /// <param name="title">The picker title.</param>
    /// <returns>The file path, or <see langword="null"/>.</returns>
    Task<string?> PickOpenFileAsync(string title);

    /// <summary>
    /// Picks a file to save.
    /// </summary>
    /// <param name="title">The picker title.</param>
    /// <param name="suggestedName">The suggested file name.</param>
    /// <returns>The file path, or <see langword="null"/>.</returns>
    Task<string?> PickSaveFileAsync(string title, string suggestedName);

    /// <summary>
    /// Picks where to save a patch; the picker starts in the downloads folder and sits on top of the dialog that is open.
    /// </summary>
    /// <param name="title">The picker title.</param>
    /// <param name="suggestedName">The suggested file name.</param>
    /// <param name="fileType">The name of the type of the file.</param>
    /// <returns>The file path, or <see langword="null"/>.</returns>
    Task<string?> PickPatchToSaveAsync(string title, string suggestedName, string fileType);

    /// <summary>
    /// Picks a patch file to open.
    /// </summary>
    /// <param name="title">The picker title.</param>
    /// <param name="fileType">The name of the type of the file.</param>
    /// <returns>The file path, or <see langword="null"/>.</returns>
    Task<string?> PickPatchToOpenAsync(string title, string fileType);

    /// <summary>
    /// Shows the window about the application.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns>A task that completes when the window is closed.</returns>
    Task ShowAboutAsync(AboutViewModel dialog);

    /// <summary>
    /// Hands an address to the system: an <c>https</c> page to the browser, a <c>mailto</c> letter to the mail program.
    /// Other schemes are refused.
    /// </summary>
    /// <param name="uri">The address.</param>
    /// <returns><see langword="true"/> when a program of the system took the address.</returns>
    Task<bool> OpenUriAsync(Uri uri);

    /// <summary>
    /// Copies text to the clipboard of the desktop.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns><see langword="true"/> when the clipboard accepted the text.</returns>
    Task<bool> CopyToClipboardAsync(string text);
}
