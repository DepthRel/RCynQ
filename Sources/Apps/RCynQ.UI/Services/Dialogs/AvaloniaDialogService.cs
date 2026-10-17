using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Platform.Storage;
using RCynQ.Core.Enums;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Wizard;
using RCynQ.UI.Views.Dialogs;

namespace RCynQ.UI.Services.Dialogs;

/// <summary>
/// <see cref="IDialogService"/> with modal Avalonia windows and the platform storage pickers.
/// </summary>
public sealed class AvaloniaDialogService : IDialogService
{
    /// <summary>Returns the owner window.</summary>
    private readonly Func<Window?> _owner;

    /// <summary>
    /// Initializes a new instance of the <see cref="AvaloniaDialogService"/> class.
    /// </summary>
    /// <param name="owner">Returns the window that owns the dialogs.</param>
    public AvaloniaDialogService(Func<Window?> owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <inheritdoc />
    public async Task<ConflictPolicy?> ShowConflictDialogAsync(ConflictDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return await ShowAsync(new ConflictDialog { DataContext = dialog }) ? dialog.Result : null;
    }

    /// <inheritdoc />
    public Task<bool> ShowExportDialogAsync(ExportDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return ShowAsync(new ExportDialog { DataContext = dialog });
    }

    /// <inheritdoc />
    public Task<bool> ShowAddNodeDialogAsync(AddNodeDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return ShowAsync(new AddNodeDialog { DataContext = dialog });
    }

    /// <inheritdoc />
    public Task<bool> ShowImportDecisionDialogAsync(ImportDecisionDialogViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return ShowAsync(new ImportDecisionDialog { DataContext = dialog });
    }

    /// <inheritdoc />
    public Task<bool> ShowConnectWizardAsync(ConnectWizardViewModel wizard)
    {
        ArgumentNullException.ThrowIfNull(wizard);
        return ShowAsync(new ConnectWizard { DataContext = wizard });
    }

    /// <inheritdoc />
    public Task ShowTextAsync(string title, string text)
        => ShowAsync(new TextDialog { DataContext = new TextDialogViewModel(title, text) });

    /// <inheritdoc />
    public Task<bool> ConfirmAsync(string title, string message)
        => ShowAsync(new ConfirmDialog { DataContext = new ConfirmDialogViewModel(title, message) });

    /// <inheritdoc />
    public async Task<string?> PickFolderAsync(string title)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count == 0 ? null : folders[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public async Task<string?> PickOpenFileAsync(string title)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("RCynQ JSON") { Patterns = new[] { "*.json" } },
                FilePickerFileTypes.All
            }
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    /// <inheritdoc />
    public Task ShowAboutAsync(AboutViewModel dialog)
    {
        ArgumentNullException.ThrowIfNull(dialog);
        return ShowAsync(new AboutDialog { DataContext = dialog });
    }

    /// <inheritdoc />
    public async Task<bool> OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // Only a web page or a letter: the address never starts a program or opens a local file.
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeMailto))
        {
            return false;
        }

        if (_owner() is not { } owner)
        {
            return false;
        }

        return await owner.Launcher.LaunchUriAsync(uri);
    }

    /// <inheritdoc />
    public async Task<bool> CopyToClipboardAsync(string text)
    {
        if (_owner()?.Clipboard is not { } clipboard)
        {
            return false;
        }

        await clipboard.SetTextAsync(text);
        return true;
    }

    /// <inheritdoc />
    public async Task<string?> PickSaveFileAsync(string title, string suggestedName)
    {
        if (_owner() is not { } owner)
        {
            return null;
        }

        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedName,
            ShowOverwritePrompt = true
        });
        return file?.TryGetLocalPath();
    }

    /// <summary>
    /// Shows a dialog window modally.
    /// </summary>
    /// <param name="window">The dialog window.</param>
    /// <returns><see langword="true"/> when the dialog was confirmed.</returns>
    private async Task<bool> ShowAsync(Window window)
    {
        if (_owner() is not { } owner)
        {
            return false;
        }

        return await window.ShowDialog<bool>(owner);
    }
}
