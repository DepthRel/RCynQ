using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.About;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Dispatching;
using RCynQ.UI.Services.Updates;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// Tells the user about a new version: it checks a while after the start and then once a day, shows a banner in the main
/// window until the user opens the website or asks not to be reminded of that version, and keeps the state for the
/// settings and the icon in the notification area.
/// </summary>
/// <remarks>
/// A version the user skipped is not offered again, but any later one is. A failed check shows no banner; only the
/// settings tell why it failed.
/// </remarks>
public sealed partial class UpdateViewModel : ObservableObject, IDisposable
{
    /// <summary>
    /// How long after the start the first check waits, so the start of the node is not slowed down by it.
    /// </summary>
    public static readonly TimeSpan FirstCheckDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How often the application checks while it runs.
    /// </summary>
    public static readonly TimeSpan CheckPeriod = TimeSpan.FromDays(1);

    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Finds out whether a newer version is published.</summary>
    private readonly IUpdateChecker _checker;

    /// <summary>The choices of the user.</summary>
    private readonly IUpdatePreferences _preferences;

    /// <summary>Opens the website.</summary>
    private readonly IDialogService _dialogs;

    /// <summary>Runs the checks of the timer on the UI thread.</summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>The clock of the timer.</summary>
    private readonly TimeProvider _time;

    /// <summary>The timer of the automatic checks, or <see langword="null"/> while they are off.</summary>
    private ITimer? _timer;

    /// <summary>The result of the last check, or <see langword="null"/> before the first one.</summary>
    private UpdateCheckResult? _last;

    /// <summary>Why the last choice of the user could not be stored, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    /// <summary>Whether a check runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(IsUpToDate))]
    [NotifyPropertyChangedFor(nameof(IsOutdated))]
    private bool _isChecking;

    /// <summary>
    /// Initializes a new instance of the <see cref="UpdateViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="checker">Finds out whether a newer version is published.</param>
    /// <param name="preferences">The choices of the user.</param>
    /// <param name="dialogs">Opens the website.</param>
    /// <param name="dispatcher">Runs the checks of the timer on the UI thread.</param>
    /// <param name="time">The clock of the timer; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public UpdateViewModel(ILocalizer localizer, IUpdateChecker checker, IUpdatePreferences preferences, IDialogService dialogs, IUiDispatcher dispatcher, TimeProvider? time = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _checker = checker ?? throw new ArgumentNullException(nameof(checker));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _dialogs = dialogs ?? throw new ArgumentNullException(nameof(dialogs));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Gets the latest published version found by the last check, or <see langword="null"/>.
    /// </summary>
    public Version? Latest => _last?.Latest;

    /// <summary>
    /// Gets a value indicating whether a newer version is published.
    /// </summary>
    public bool IsAvailable => _last is { Status: UpdateCheckStatus.Available, Latest: not null };

    /// <summary>
    /// Gets a value indicating whether the line of the settings tells that the latest version runs (shown in green).
    /// </summary>
    public bool IsUpToDate => !IsChecking && _last is { Status: UpdateCheckStatus.UpToDate };

    /// <summary>
    /// Gets a value indicating whether the line of the settings tells about a newer version (shown in red).
    /// </summary>
    public bool IsOutdated => !IsChecking && IsAvailable;

    /// <summary>
    /// Gets a value indicating whether the banner of the main window is shown: a newer version that the user did not ask
    /// to skip. A version later than the skipped one is shown again.
    /// </summary>
    public bool IsBannerVisible => IsAvailable && (_preferences.SkippedVersion is not { } skipped || Latest! > skipped);

    /// <summary>
    /// Gets the text of the banner.
    /// </summary>
    public string BannerText => IsAvailable ? _localizer.Format("Update.Available", Latest!.ToString(3), _checker.Current.ToString(3)) : string.Empty;

    /// <summary>
    /// Gets the line of the settings about the version: what the last check found.
    /// </summary>
    public string StatusText => IsChecking
        ? _localizer.Get("Update.Checking")
        : _last?.Status switch
        {
            UpdateCheckStatus.UpToDate => _localizer.Get("Update.UpToDate"),
            UpdateCheckStatus.Available => _localizer.Format("Update.Outdated", Latest!.ToString(3), _checker.Current.ToString(3)),
            UpdateCheckStatus.Failed => _localizer.Format("Update.Failed", _last.Error ?? string.Empty),
            _ => _localizer.Get("Update.NotChecked"),
        };

    /// <summary>
    /// Gets the hint of the icon in the notification area.
    /// </summary>
    public string TrayToolTip => IsAvailable ? _localizer.Format("Update.Tray", Latest!.ToString(3)) : "RCynQ";

    /// <summary>
    /// Gets a value indicating whether a choice could not be stored.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>
    /// Gets or sets a value indicating whether the application looks for a new version by itself; a new value is stored
    /// at once and starts or stops the checks.
    /// </summary>
    public bool CheckEnabled
    {
        get => _preferences.CheckEnabled;
        set
        {
            if (value == _preferences.CheckEnabled)
            {
                return;
            }

            try
            {
                _preferences.SetCheckEnabled(value);
                Error = null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Error = _localizer.Describe(ex);
            }

            Schedule();
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Starts the automatic checks when they are on.
    /// </summary>
    public void Start() => Schedule();

    /// <summary>
    /// Tells the bindings that the texts computed from the language changed.
    /// </summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <inheritdoc />
    public void Dispose()
    {
        _timer?.Dispose();
        _timer = null;
    }

    /// <summary>
    /// Checks now, whatever the setting of the automatic checks.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [RelayCommand]
    private async Task CheckNowAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            _last = await _checker.CheckAsync();
        }
        finally
        {
            IsChecking = false;
        }

        OnPropertyChanged(string.Empty);
    }

    /// <summary>
    /// Opens the downloads of the project page. The address is the one of the project, never one read from the network.
    /// </summary>
    /// <returns>A task that completes when the browser was asked to open the page.</returns>
    [RelayCommand]
    private Task OpenSiteAsync() => _dialogs.OpenUriAsync(new Uri(ProjectSite.DOWNLOAD_URL));

    /// <summary>
    /// Stops reminding of the version found; a later one is offered again.
    /// </summary>
    [RelayCommand]
    private void Skip()
    {
        if (Latest is not { } latest)
        {
            return;
        }

        try
        {
            _preferences.Skip(latest);
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = _localizer.Describe(ex);
        }

        OnPropertyChanged(nameof(IsBannerVisible));
    }

    /// <summary>
    /// Starts the timer of the automatic checks when they are on, and stops it when they are off.
    /// </summary>
    private void Schedule()
    {
        _timer?.Dispose();
        _timer = _preferences.CheckEnabled
            ? _time.CreateTimer(_ => _dispatcher.Post(() => _ = CheckNowAsync()), null, FirstCheckDelay, CheckPeriod)
            : null;
    }
}
