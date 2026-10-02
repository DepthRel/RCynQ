using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The settings tab: the language of the interface and the cleanup of the data the node has accumulated.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="language">The language selector, shared with the other places that show it.</param>
    /// <param name="api">The service API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="settingsPath">The file the settings are stored in, shown to the user, or <see langword="null"/>.</param>
    public SettingsViewModel(ILocalizer localizer, LanguageViewModel language, IServiceApiClient api, IDialogService dialogs, string? settingsPath = null)
        : base(localizer)
    {
        Language = language ?? throw new ArgumentNullException(nameof(language));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        SettingsPath = settingsPath;
    }

    /// <summary>
    /// Gets the language selector.
    /// </summary>
    public LanguageViewModel Language { get; }

    /// <summary>
    /// Gets the file the settings are stored in, or <see langword="null"/> when it is not known.
    /// </summary>
    public string? SettingsPath { get; }

    /// <summary>
    /// Gets a value indicating whether the location of the settings is shown.
    /// </summary>
    public bool HasSettingsPath => !string.IsNullOrEmpty(SettingsPath);

    /// <summary>
    /// Gets the description of where the settings are stored.
    /// </summary>
    public string StoredInText => SettingsPath is null ? string.Empty : Localizer.Format("Settings.StoredIn", SettingsPath);

    /// <inheritdoc />
    public override Task RefreshAsync() => Task.CompletedTask;

    /// <summary>
    /// Describes what a cleanup removed.
    /// </summary>
    /// <param name="report">The report of the service.</param>
    /// <returns>The text shown to the user.</returns>
    public string Describe(Core.Api.DataCleanupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.RemovedAnything
            ? Localizer.Format(
                "Settings.Data.Done",
                report.Patches,
                report.HistoryEntries,
                report.DiagnosticRuns,
                report.Backups,
                report.TemporaryItems,
                report.FreedBytes / (1024d * 1024d))
            : Localizer.Get("Settings.Data.Nothing");
    }

    /// <summary>
    /// Removes the finished data of the node after the user confirmed it.
    /// </summary>
    /// <returns>A task representing the command.</returns>
    [RelayCommand]
    private async Task CleanDataAsync()
    {
        if (!await _dialogs.ConfirmAsync(Localizer.Get("Settings.Data.Clean"), Localizer.Get("Settings.Data.Confirm")))
        {
            return;
        }

        await RunSafeAsync(async () => InfoMessage = Describe(await _api.CleanDataAsync()));
    }
}
