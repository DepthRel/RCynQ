using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Localization;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The settings tab: the language of the interface, the cleanup of the data the node has accumulated, and the part
/// about the application with the way to report a problem.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>What the user can do with the project: its website, its source code, a letter about a problem.</summary>
    private readonly ProjectActions _project;

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
        _project = new ProjectActions(localizer, dialogs);
        SettingsPath = settingsPath;
    }

    /// <summary>
    /// Gets the line with the version of the application, shown in the part about the application.
    /// </summary>
    public string VersionText => Localizer.Format("About.Version", AppInfo.Version);

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

    /// <summary>
    /// Gets a value indicating whether the node is an Origin, the node that may take commits from the Workstation.
    /// </summary>
    public bool CanAcceptCommits { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this Origin takes commits from the Workstation; saved by <see cref="SaveAcceptCommitsCommand"/>.
    /// </summary>
    public bool AcceptCommits { get; set; }

    /// <inheritdoc />
    protected override Task<bool> LoadAsync() => Task.FromResult(true);

    /// <summary>
    /// Shows the settings of the node the service runs, from the configuration the application loaded at the start.
    /// </summary>
    /// <param name="config">The configuration of the node.</param>
    public void Configure(NodeConfigurationDto config)
    {
        ArgumentNullException.ThrowIfNull(config);
        CanAcceptCommits = config.Capabilities.ReceivesCommits;
        AcceptCommits = config.AcceptCommits;
        OnPropertyChanged(nameof(CanAcceptCommits));
        OnPropertyChanged(nameof(AcceptCommits));
    }

    /// <summary>
    /// Saves whether this Origin takes commits from the Workstation, as the user just switched it; switches it back when
    /// the service could not save it.
    /// </summary>
    /// <returns>A task representing the command.</returns>
    [RelayCommand]
    private async Task SaveAcceptCommitsAsync()
    {
        var enabled = AcceptCommits;
        if (await RunSafeAsync(() => _api.SetAcceptCommitsAsync(enabled)))
        {
            InfoMessage = Localizer.Get(enabled ? "Settings.Commits.On" : "Settings.Commits.Off");
            NotifyDataChanged();
            return;
        }

        AcceptCommits = !enabled;
        OnPropertyChanged(nameof(AcceptCommits));
    }

    /// <summary>
    /// Describes what a cleanup removed.
    /// </summary>
    /// <param name="report">The report of the service.</param>
    /// <returns>The text shown to the user.</returns>
    public string Describe(DataCleanupReport report)
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

        if (await RunSafeAsync(async () => InfoMessage = Describe(await _api.CleanDataAsync())))
        {
            NotifyDataChanged();
        }
    }

    /// <summary>
    /// Opens the window about the application.
    /// </summary>
    /// <returns>A task that completes when the window is closed.</returns>
    [RelayCommand]
    private Task OpenAboutAsync() => _dialogs.ShowAboutAsync(new AboutViewModel(Localizer, _project));

    /// <summary>
    /// Opens a letter about a problem in the mail program of the user.
    /// </summary>
    /// <returns>A task that completes when the letter is open or the user answered.</returns>
    [RelayCommand]
    private Task ReportProblemAsync() => _project.ReportProblemAsync();
}
