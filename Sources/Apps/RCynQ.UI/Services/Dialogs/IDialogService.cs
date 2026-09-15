using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Dialogs;

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
    /// <returns>The chosen policy, or <see langword="null"/> when cancelled.</returns>
    Task<ConflictPolicy?> ShowConflictDialogAsync(ConflictDialogViewModel dialog);

    /// <summary>
    /// Shows the export dialog that asks for the advertised SSH address.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ShowExportDialogAsync(ExportDialogViewModel dialog);

    /// <summary>
    /// Shows the dialog that adds a chain node.
    /// </summary>
    /// <param name="dialog">The dialog view model.</param>
    /// <returns><see langword="true"/> when the user confirmed.</returns>
    Task<bool> ShowAddNodeDialogAsync(AddNodeDialogViewModel dialog);

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
}
