using System.Globalization;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Localization;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Reset;
using RCynQ.UI.Services.Shell;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.ViewModels.Pages;

/// <summary>
/// The settings tab: the language of the interface, what closing the window does, the cleanup of the data the node has
/// accumulated, the part about the application with the way to report a problem, and the danger zone with the reset of
/// the node.
/// </summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    /// <summary>The service API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The dialog service.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>What the user can do with the project: its website, its source code, a letter about a problem.</summary>
    private readonly ProjectActions _project;

    /// <summary>Whether closing the window keeps the application running, or <see langword="null"/> when it is not offered.</summary>
    private readonly ITrayPreferences? _tray;

    /// <summary>Resets the node, or <see langword="null"/> when the reset is not offered.</summary>
    private readonly INodeResetCoordinator? _reset;

    /// <summary>How long the pattern of the singular form may take to match a number.</summary>
    private static readonly TimeSpan _patternTimeout = TimeSpan.FromMilliseconds(100);

    /// <summary>The most digits the box of the days takes: up to 9999 days.</summary>
    private const int MAX_RETENTION_DIGITS = 4;

    /// <summary>Whether the node removes old finished data by itself, as the switch shows it.</summary>
    private bool _autoClean = true;

    /// <summary>What the box of the days holds: digits only, possibly none.</summary>
    private string _retentionDaysText = SyncBridgeOptions.DEFAULT_RETENTION_DAYS.ToString(CultureInfo.InvariantCulture);

    /// <summary>The number of days the service uses now; <c>0</c> keeps the data.</summary>
    private int _savedRetentionDays = SyncBridgeOptions.DEFAULT_RETENTION_DAYS;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="language">The language selector, shared with the other places that show it.</param>
    /// <param name="api">The service API client.</param>
    /// <param name="dialogs">The dialog service.</param>
    /// <param name="mergeTool">The choice of the merge tool, or <see langword="null"/> when it is not offered.</param>
    /// <param name="tray">Whether closing the window keeps the application running, or <see langword="null"/> when it is not offered.</param>
    /// <param name="reset">Resets the node, or <see langword="null"/> when the reset is not offered.</param>
    /// <param name="updates">Tells about a new version, or <see langword="null"/> when it is not offered.</param>
    public SettingsViewModel(
        ILocalizer localizer,
        LanguageViewModel language,
        IServiceApiClient api,
        IDialogService dialogs,
        MergeToolSettingsViewModel? mergeTool = null,
        ITrayPreferences? tray = null,
        INodeResetCoordinator? reset = null,
        UpdateViewModel? updates = null)
        : base(localizer)
    {
        Updates = updates;
        Language = language ?? throw new ArgumentNullException(nameof(language));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _project = new ProjectActions(localizer, dialogs);
        MergeTool = mergeTool;
        _tray = tray;
        _reset = reset;
    }

    /// <summary>
    /// Raised after a reset of the node ran, complete or not: the window then shows the first setup, or the node again.
    /// </summary>
    public event EventHandler<NodeResetOutcome>? ResetCompleted;

    /// <summary>
    /// Gets what tells about a new version of the application, or <see langword="null"/> when it is not offered.
    /// </summary>
    public UpdateViewModel? Updates { get; }

    /// <summary>
    /// Gets a value indicating whether the part about new versions is shown.
    /// </summary>
    public bool HasUpdates => Updates is not null;

    /// <summary>
    /// Gets a value indicating whether the danger zone with the reset of the node is shown.
    /// </summary>
    public bool HasReset => _reset is not null;

    /// <summary>
    /// Gets a value indicating whether the node can be reset from here: only the service this application started.
    /// </summary>
    public bool CanReset => _reset?.CanReset ?? false;

    /// <summary>
    /// Gets a value indicating whether the choice of what closing the window does is shown.
    /// </summary>
    public bool HasTray => _tray is not null;

    /// <summary>
    /// Gets or sets a value indicating whether closing the window only hides it in the notification area; a new value is
    /// stored at once, and a value that could not be stored is put back with the reason.
    /// </summary>
    public bool CloseToTray
    {
        get => _tray?.CloseToTray ?? false;
        set
        {
            if (_tray is null || value == _tray.CloseToTray)
            {
                return;
            }

            try
            {
                _tray.SetCloseToTray(value);
                ErrorMessage = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                ErrorMessage = Localizer.Describe(ex);
            }

            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Gets the choice of the merge tool conflicts are resolved in, or <see langword="null"/> when it is not offered.
    /// </summary>
    public MergeToolSettingsViewModel? MergeTool { get; }

    /// <summary>
    /// Gets a value indicating whether the choice of the merge tool is shown.
    /// </summary>
    public bool HasMergeTool => MergeTool is not null;

    /// <summary>
    /// Gets the line with the version of the application, shown in the part about the application.
    /// </summary>
    public string VersionText => Localizer.Format("About.Version", AppInfo.Version);

    /// <summary>
    /// Gets the language selector.
    /// </summary>
    public LanguageViewModel Language { get; }

    /// <summary>
    /// Gets a value indicating whether the node is an Origin, the node that may take commits from the Workstation.
    /// </summary>
    public bool CanAcceptCommits { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether this Origin takes commits from the Workstation; saved by <see cref="SaveAcceptCommitsCommand"/>.
    /// </summary>
    public bool AcceptCommits { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the node removes old finished data by itself; a new value is saved at once
    /// with the number of days in the box (or the default one when the box holds none), and put back when the service
    /// could not save it.
    /// </summary>
    public bool AutoClean
    {
        get => _autoClean;
        set
        {
            if (value == _autoClean)
            {
                return;
            }

            _autoClean = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRetentionError));
            if (!value)
            {
                _ = SaveRetentionAsync(0);
                return;
            }

            if (ParseDays(_retentionDaysText) is not { } days)
            {
                days = SyncBridgeOptions.DEFAULT_RETENTION_DAYS;
                _retentionDaysText = days.ToString(CultureInfo.InvariantCulture);
                OnPropertyChanged(nameof(RetentionDaysText));
                OnPropertyChanged(nameof(HasRetentionError));
            }

            _ = SaveRetentionAsync(days);
        }
    }

    /// <summary>
    /// Gets or sets the number of days finished data is kept, as typed: only digits stay. A valid number is saved at once;
    /// an empty box or a zero is marked as an error and changes nothing.
    /// </summary>
    public string RetentionDaysText
    {
        get => _retentionDaysText;
        set
        {
            var digits = new string((value ?? string.Empty).Where(char.IsAsciiDigit).Take(MAX_RETENTION_DIGITS).ToArray());
            if (digits == _retentionDaysText)
            {
                // The box may still show what was taken out; telling the binding brings it back to the digits.
                OnPropertyChanged();
                return;
            }

            _retentionDaysText = digits;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasRetentionError));
            if (_autoClean && ParseDays(digits) is { } days && days != _savedRetentionDays)
            {
                _ = SaveRetentionAsync(days);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether the box of the days holds no valid number while the automatic cleanup is on.
    /// </summary>
    public bool HasRetentionError => _autoClean && ParseDays(_retentionDaysText) is null;

    /// <summary>
    /// Gets the description of what happens to the finished data, with the number of days the node really uses in the
    /// form of the word the number needs ("1 day", "5 days").
    /// </summary>
    public string DataHintText
    {
        get
        {
            if (_savedRetentionDays <= 0)
            {
                return Localizer.Get("Settings.Data.HintManual");
            }

            var key = TakesSingular(_savedRetentionDays) ? "Settings.Data.HintAutoOne" : "Settings.Data.HintAuto";
            return Localizer.Format(key, _savedRetentionDays);
        }
    }

    /// <summary>
    /// Tells whether a number takes the singular form of the word after it in the language of the interface. The rule is
    /// a pattern of the language pack (<c>Plural.One</c>), matched against the digits of the number.
    /// </summary>
    /// <param name="number">The number.</param>
    /// <returns><see langword="true"/> for the singular form.</returns>
    private bool TakesSingular(int number)
    {
        try
        {
            return Regex.IsMatch(number.ToString(CultureInfo.InvariantCulture), Localizer.Get("Plural.One"), RegexOptions.CultureInvariant, _patternTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            // A broken pattern in a pack gives the plural form: the sentence stays readable.
            return false;
        }
    }

    /// <inheritdoc />
    public override void RefreshTexts()
    {
        base.RefreshTexts();

        // The entry of the tool the user describes is named in the language of the interface.
        _ = MergeTool?.LoadAsync();
    }

    /// <inheritdoc />
    protected override async Task<bool> LoadAsync()
    {
        // The search of the machine is kept after the first time, so showing the page again is quick.
        if (MergeTool is not null)
        {
            await MergeTool.LoadAsync();
        }

        return true;
    }

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

        // The box keeps a number to come back to even while the cleanup is off.
        _savedRetentionDays = Math.Max(0, config.RetentionDays);
        _autoClean = _savedRetentionDays > 0;
        _retentionDaysText = (_autoClean ? _savedRetentionDays : SyncBridgeOptions.DEFAULT_RETENTION_DAYS).ToString(CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(AutoClean));
        OnPropertyChanged(nameof(RetentionDaysText));
        OnPropertyChanged(nameof(HasRetentionError));
        OnPropertyChanged(nameof(DataHintText));

        // The service may have been started by this application or found running: only the first can be reset here.
        OnPropertyChanged(nameof(CanReset));
    }

    /// <summary>
    /// Resets the node after the user typed its identifier: its lines in <c>authorized_keys</c>, its configuration and
    /// its data go, and the window shows the first setup.
    /// </summary>
    /// <returns>A task representing the command.</returns>
    [RelayCommand]
    private async Task ResetNodeAsync()
    {
        if (_reset is null || !_reset.CanReset)
        {
            return;
        }

        if (_reset.Plan() is not { } plan)
        {
            ErrorMessage = Localizer.Get("Reset.NoConfig");
            return;
        }

        if (!await _dialogs.ShowResetNodeDialogAsync(new ResetNodeDialogViewModel(Localizer, plan)))
        {
            return;
        }

        NodeResetOutcome? outcome = null;
        if (await RunSafeAsync(async () => outcome = await _reset.ResetAsync(plan)) && outcome is not null)
        {
            if (!outcome.Report.IsComplete)
            {
                ErrorMessage = Localizer.Format("Reset.Incomplete", string.Join(Environment.NewLine, outcome.Report.Failed));
            }

            ResetCompleted?.Invoke(this, outcome);
        }
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
    /// Saves for how many days the node keeps finished data; when the service cannot save it, the switch and the box show
    /// the value the service still uses.
    /// </summary>
    /// <param name="days">The number of days; <c>0</c> keeps the data.</param>
    /// <returns>A task that completes when the value is saved or put back.</returns>
    private async Task SaveRetentionAsync(int days)
    {
        if (await RunSafeAsync(() => _api.SetRetentionDaysAsync(days)))
        {
            _savedRetentionDays = days;
        }
        else
        {
            _autoClean = _savedRetentionDays > 0;
            if (_autoClean)
            {
                _retentionDaysText = _savedRetentionDays.ToString(CultureInfo.InvariantCulture);
            }

            OnPropertyChanged(nameof(AutoClean));
            OnPropertyChanged(nameof(RetentionDaysText));
            OnPropertyChanged(nameof(HasRetentionError));
        }

        OnPropertyChanged(nameof(DataHintText));
    }

    /// <summary>
    /// Reads the number of days from the box.
    /// </summary>
    /// <param name="text">The digits of the box.</param>
    /// <returns>The number of days, or <see langword="null"/> when the box is empty or holds zero.</returns>
    private static int? ParseDays(string text)
        => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var days) && days > 0 ? days : null;

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
