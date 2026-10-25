using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dispatching;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.ViewModels.Repositories;
using RCynQ.UI.ViewModels.Setup;

namespace RCynQ.UI.ViewModels;

/// <summary>
/// The main window: starts the local service, runs the first-run setup, adapts the tabs to the role and routes live
/// service events to the pages.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The event stream.</summary>
    private readonly INodeEventStream _events;

    /// <summary>The UI dispatcher.</summary>
    private readonly IUiDispatcher _dispatcher;

    /// <summary>The role adapter.</summary>
    private readonly RoleAdapter _roleAdapter;

    /// <summary>The service controller.</summary>
    private readonly IServiceController _controller;

    /// <summary>All tabs.</summary>
    private readonly List<NavigationItemViewModel> _allTabs;

    /// <summary>Whether the node configuration has been loaded from the service.</summary>
    private bool _initialized;

    /// <summary>Whether the live event stream went down after the pages were loaded.</summary>
    private bool _eventsLost;

    /// <summary>The data directory checked for a copied installation, or <see langword="null"/>.</summary>
    private string? _dataRoot;

    /// <summary>Produces the connection status text in the current language.</summary>
    private Func<string> _connectionText;

    /// <summary>Produces the window title in the current language.</summary>
    private Func<string> _nodeTitle = () => "RCynQ";

    /// <summary>The name of the computer the installation was copied from, or <see langword="null"/>.</summary>
    private string? _installationCopiedFrom;

    /// <summary>The service status shown in the status bar.</summary>
    private ServiceControllerStatus _serviceStatus = ServiceControllerStatus.NotStarted;

    /// <summary>The selected tab.</summary>
    [ObservableProperty]
    private NavigationItemViewModel? _selectedTab;

    /// <summary>Whether the live event stream is connected.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>Whether the first-run setup is shown instead of the tabs.</summary>
    [ObservableProperty]
    private bool _isSetupVisible;

    /// <summary>Whether the service is being started or restarted, so the page area shows that work is going on.</summary>
    [ObservableProperty]
    private bool _isStarting;

    /// <summary>The problem that prevents using the service, shown once instead of per-page errors.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupError))]
    private string? _startupError;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="api">The API client.</param>
    /// <param name="events">The live event stream.</param>
    /// <param name="dispatcher">The UI dispatcher.</param>
    /// <param name="roleAdapter">The role adapter.</param>
    /// <param name="controller">The service controller.</param>
    /// <param name="setup">The first-run setup.</param>
    /// <param name="overview">The overview page.</param>
    /// <param name="roleAndNodes">The role and nodes page.</param>
    /// <param name="sync">The synchronization page.</param>
    /// <param name="tunnel">The tunnel page.</param>
    /// <param name="diagnostics">The diagnostics page.</param>
    /// <param name="branches">The branches page.</param>
    /// <param name="logs">The logs page.</param>
    /// <param name="settings">The settings page.</param>
    /// <param name="repositories">The repositories of the node; <see langword="null"/> shows no switcher.</param>
    public MainViewModel(
        ILocalizer localizer,
        IServiceApiClient api,
        INodeEventStream events,
        IUiDispatcher dispatcher,
        RoleAdapter roleAdapter,
        IServiceController controller,
        SetupViewModel setup,
        OverviewViewModel overview,
        RoleAndNodesViewModel roleAndNodes,
        SyncViewModel sync,
        TunnelViewModel tunnel,
        DiagnosticsViewModel diagnostics,
        BranchViewModel branches,
        LogsViewModel logs,
        SettingsViewModel settings,
        RepositoriesViewModel? repositories = null)
    {
        Repositories = repositories;
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _connectionText = () => _localizer.Get("Main.Connecting");
        _api = api;
        _events = events;
        _dispatcher = dispatcher;
        _roleAdapter = roleAdapter;
        _controller = controller;
        Setup = setup;
        Overview = overview;
        RoleAndNodes = roleAndNodes;
        Sync = sync;
        Tunnel = tunnel;
        Diagnostics = diagnostics;
        Branches = branches;
        Logs = logs;
        Settings = settings;

        _allTabs = new List<NavigationItemViewModel>
        {
            new(_localizer, TabKey.Overview, "Icon.Home", overview),
            new(_localizer, TabKey.RoleAndNodes, "Icon.Nodes", roleAndNodes),
            new(_localizer, TabKey.Sync, "Icon.Upload", sync),
            new(_localizer, TabKey.Tunnel, "Icon.Tunnel", tunnel),
            new(_localizer, TabKey.Diagnostics, "Icon.Stethoscope", diagnostics),
            new(_localizer, TabKey.Branches, "Icon.Branch", branches),
            new(_localizer, TabKey.Logs, "Icon.Logs", logs),
            new(_localizer, TabKey.Settings, "Icon.Settings", settings)
        };

        foreach (var tab in _allTabs)
        {
            Tabs.Add(tab);

            // An action on one page changes what other pages show; they reload when they are opened.
            var page = tab.Page;
            page.DataChanged += (_, _) => InvalidatePages(except: page);
        }

        overview.NavigationRequested += (_, key) => Navigate(key);
        roleAndNodes.RestartRequested += (_, _) => _ = RestartServiceAsync();
        setup.Completed += (_, _) => _ = CompleteSetupAsync();
        _controller.StatusChanged += (_, status) => _dispatcher.Post(() =>
        {
            _serviceStatus = status;
            OnPropertyChanged(nameof(ServiceText));
        });
        _localizer.LanguageChanged += (_, _) => _dispatcher.Post(OnLanguageChanged);
        _events.LinkStateChanged += (_, evt) => _dispatcher.Post(() => OnLinkStateChanged(evt));
        _events.PatchChanged += (_, evt) => _dispatcher.Post(() => OnPatchChanged(evt));
        _events.DiagnosticsCompleted += (_, evt) => _dispatcher.Post(() => OnDiagnosticsCompleted(evt));
        _events.ConnectionChanged += (_, connected) => _dispatcher.Post(() => SetConnected(connected));
        _events.RepositoryChanged += (_, evt) => _dispatcher.Post(() => Repositories?.ApplyEvent(evt));
        _events.LinkRoutesChanged += (_, evt) => _dispatcher.Post(() => RoleAndNodes.ApplyRoutes(evt.Routes));
        _events.JoinProgress += (_, evt) => _dispatcher.Post(() => RoleAndNodes.ApplyJoinProgress(evt));
        branches.Repositories = repositories;
        if (repositories is not null)
        {
            repositories.Switched += (_, added) => OnRepositorySwitched(added);
        }
    }

    /// <summary>Gets the first-run setup.</summary>
    public SetupViewModel Setup { get; }

    /// <summary>Gets the overview page.</summary>
    public OverviewViewModel Overview { get; }

    /// <summary>Gets the role and nodes page.</summary>
    public RoleAndNodesViewModel RoleAndNodes { get; }

    /// <summary>Gets the synchronization page.</summary>
    public SyncViewModel Sync { get; }

    /// <summary>Gets the tunnel page.</summary>
    public TunnelViewModel Tunnel { get; }

    /// <summary>Gets the diagnostics page.</summary>
    public DiagnosticsViewModel Diagnostics { get; }

    /// <summary>Gets the branches page.</summary>
    public BranchViewModel Branches { get; }

    /// <summary>Gets the logs page.</summary>
    public LogsViewModel Logs { get; }

    /// <summary>Gets the settings page.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Gets the repositories of the node and the switcher between them, or <see langword="null"/>.</summary>
    public RepositoriesViewModel? Repositories { get; }

    /// <summary>Gets the connection status text.</summary>
    public string ConnectionText => _connectionText();

    /// <summary>Gets the service status text.</summary>
    public string ServiceText => FormatService(_serviceStatus);

    /// <summary>Gets the node title shown in the window title.</summary>
    public string NodeTitle => _nodeTitle();

    /// <summary>Gets the warning shown when the installation was copied from another computer together with its data.</summary>
    public string? InstallationWarning => _installationCopiedFrom is null ? null : _localizer.Format("Main.InstallationCopied", _installationCopiedFrom);

    /// <summary>Gets all tabs in display order; the view moves tabs that do not fit into the "…" menu.</summary>
    public ObservableCollection<NavigationItemViewModel> Tabs { get; } = new();

    /// <summary>Gets a value indicating whether a service start problem is shown.</summary>
    public bool HasStartupError => !string.IsNullOrEmpty(StartupError);

    /// <summary>Gets a value indicating whether the copied-installation warning is shown.</summary>
    public bool HasInstallationWarning => !string.IsNullOrEmpty(InstallationWarning);

    /// <summary>Gets the page of the selected tab.</summary>
    public PageViewModelBase? CurrentPage => SelectedTab?.Page;

    /// <summary>
    /// Starts the service (or shows the setup when there is no valid configuration), loads the configuration, adapts the
    /// tabs to the role and connects to live events.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async Task InitializeAsync()
    {
        if (!File.Exists(_controller.ConfigPath))
        {
            ShowSetup(null);
            return;
        }

        SetConnectionText(() => _localizer.Get("Main.StartingService"));
        IsStarting = true;
        var role = NodeRole.Workstation;
        try
        {
            var start = await _controller.EnsureRunningAsync();
            if (start.IsConfigurationError)
            {
                ShowSetup(start.Message);
                return;
            }

            IsSetupVisible = false;
            CheckInstallation();
            try
            {
                var config = await _api.GetConfigAsync();
                role = config.Role;
                SetNodeTitle(() => $"RCynQ — {config.Name} ({config.Role})");
                Branches.Configure(config.Capabilities);
                Overview.Configure(config);
                Settings.Configure(config);
                Repositories?.Configure(config.Role);
                SetConnectionText(() => start.Message);
                StartupError = null;
                _initialized = true;
            }
            catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
            {
                _initialized = false;
                SetConnectionText(start.IsRunning ? () => _localizer.Format("Main.ServiceUnreachable", _localizer.Describe(ex)) : () => start.Message);
                StartupError = ConnectionText;
            }
        }
        finally
        {
            IsStarting = false;
        }

        // The service was started or restarted: whatever the pages show belongs to the service that ran before.
        InvalidatePages(except: null);
        var tab = _roleAdapter.Apply(_allTabs, role);
        if (ReferenceEquals(tab, SelectedTab))
        {
            if (_initialized)
            {
                _ = tab?.Page.EnsureLoadedAsync();
            }
        }
        else
        {
            SelectedTab = tab;
        }

        if (!_initialized)
        {
            return;
        }

        // In the background: what the Origin offers is asked through the link, which may take a while.
        _ = Repositories?.LoadAsync();

        try
        {
            await _events.ConnectAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or IOException)
        {
            SetConnected(false);
        }
    }

    /// <summary>
    /// Selects the tab with the given key when it is visible.
    /// </summary>
    /// <param name="key">The tab key.</param>
    public void Navigate(TabKey key)
    {
        var tab = _allTabs.FirstOrDefault(item => item.Key == key && item.IsAvailable);
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    /// <summary>
    /// Updates selection flags and loads the page of the new tab.
    /// </summary>
    /// <param name="oldValue">The previous tab.</param>
    /// <param name="newValue">The new tab.</param>
    partial void OnSelectedTabChanged(NavigationItemViewModel? oldValue, NavigationItemViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsSelected = true;
            newValue.HasAttention = false;
            if (_initialized)
            {
                // Loads the page the first time it is opened, or when its data went out of date; otherwise nothing is asked.
                _ = newValue.Page.EnsureLoadedAsync();
            }
        }

        OnPropertyChanged(nameof(CurrentPage));
    }

    /// <summary>
    /// Formats the service status for the status bar.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The text.</returns>
    private string FormatService(ServiceControllerStatus status)
    {
        var text = DisplayText.Service(_localizer, status);
        return text.Length == 0 ? string.Empty : "· " + text;
    }

    /// <summary>
    /// Sets the connection status text; the text is produced again when the language changes.
    /// </summary>
    /// <param name="text">Produces the text.</param>
    private void SetConnectionText(Func<string> text)
    {
        _connectionText = text;
        OnPropertyChanged(nameof(ConnectionText));
    }

    /// <summary>
    /// Sets the window title; the title is produced again when the language changes.
    /// </summary>
    /// <param name="text">Produces the title.</param>
    private void SetNodeTitle(Func<string> text)
    {
        _nodeTitle = text;
        OnPropertyChanged(nameof(NodeTitle));
    }

    /// <summary>
    /// Puts every text on the screen into the new language: the computed texts of the window and of the pages, and the
    /// rows of the page in view, which are read again so the texts the service words follow the language too.
    /// </summary>
    private void OnLanguageChanged()
    {
        OnPropertyChanged(string.Empty);
        Setup.RefreshTexts();
        Repositories?.RefreshTexts();
        foreach (var tab in _allTabs)
        {
            tab.RefreshTexts();
            tab.Page.RefreshTexts();
        }

        // The texts the service words are read again: now for the page in view, for the others when they are opened.
        InvalidatePages(except: CurrentPage);
        if (_initialized)
        {
            _ = CurrentPage?.RefreshAsync();
        }
    }

    /// <summary>
    /// Reloads the current page, or retries initialization (including starting the service) when it failed before.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (!_initialized || !_events.IsConnected)
        {
            await InitializeAsync();
            return;
        }

        if (CurrentPage is not null)
        {
            await CurrentPage.ReloadAsync();
        }
    }

    /// <summary>
    /// Selects a tab.
    /// </summary>
    /// <param name="tab">The tab.</param>
    [RelayCommand]
    private void SelectTab(NavigationItemViewModel? tab)
    {
        if (tab is { IsAvailable: true })
        {
            SelectedTab = tab;
        }
    }

    /// <summary>
    /// Warns when a portable installation carries the data of a node created on another computer.
    /// </summary>
    private void CheckInstallation()
    {
        var portableRoot = ConfigLocations.FindPortableRoot();
        if (portableRoot is null)
        {
            return;
        }

        var options = LocalConfigFile.TryLoad(_controller.ConfigPath) ?? new SyncBridgeOptions();
        _dataRoot = ConfigLocations.ResolveDataRoot(options, _controller.ConfigPath, portableRoot);
        var check = InstallationStamp.Check(_dataRoot, Environment.MachineName);
        _installationCopiedFrom = check.CopiedFromAnotherMachine ? check.RecordedMachineName : null;
        OnPropertyChanged(nameof(InstallationWarning));
        OnPropertyChanged(nameof(HasInstallationWarning));
    }

    /// <summary>
    /// Confirms that a copied installation is the same node moved to this computer.
    /// </summary>
    [RelayCommand]
    private void AcceptInstallation()
    {
        if (_dataRoot is not null)
        {
            InstallationStamp.Write(_dataRoot, Environment.MachineName);
        }

        _installationCopiedFrom = null;
        OnPropertyChanged(nameof(InstallationWarning));
        OnPropertyChanged(nameof(HasInstallationWarning));
    }

    /// <summary>
    /// Shows the first-run setup.
    /// </summary>
    /// <param name="error">The problem that opened the setup, or <see langword="null"/>.</param>
    private void ShowSetup(string? error)
    {
        Setup.Load(LocalConfigFile.TryLoad(_controller.ConfigPath), error);
        StartupError = null;
        IsSetupVisible = true;
        SetNodeTitle(() => _localizer.Get("Main.NodeTitleSetup"));
        SetConnectionText(error is null ? () => _localizer.Get("Main.SetupNeeded") : () => _localizer.Get("Main.ConfigRejected"));
    }

    /// <summary>
    /// Starts the service with the configuration written by the setup.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task CompleteSetupAsync()
    {
        IsSetupVisible = false;
        if (_controller.IsManaged)
        {
            SetConnectionText(() => _localizer.Get("Main.RestartingService"));
            IsStarting = true;
            ServiceStartResult restart;
            try
            {
                restart = await _controller.RestartAsync();
            }
            finally
            {
                IsStarting = false;
            }

            if (restart.IsConfigurationError)
            {
                ShowSetup(restart.Message);
                return;
            }
        }

        _initialized = false;
        await InitializeAsync();
        await OfferConnectionAsync();
    }

    /// <summary>
    /// Opens the connect wizard after the first setup when the node has no links yet: the next thing to do is to link it.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task OfferConnectionAsync()
    {
        try
        {
            if ((await _api.GetLinksAsync()).Count > 0)
            {
                return;
            }
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
        {
            return;
        }

        await RoleAndNodes.RefreshAsync();
        await RoleAndNodes.OpenConnectWizardAsync();
    }

    /// <summary>
    /// Restarts the service after a saved role change and adapts the tabs to the new role.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task RestartServiceAsync()
    {
        SetConnectionText(() => _localizer.Get("Main.RestartingNewConfig"));
        var result = await _controller.RestartAsync();
        if (result.Outcome == ServiceStartOutcome.NotManaged)
        {
            SetConnectionText(() => result.Message);
            RoleAndNodes.InfoMessage = _localizer.Format("Main.RoleSaved", result.Message);
            return;
        }

        if (result.IsConfigurationError)
        {
            ShowSetup(result.Message);
            return;
        }

        _initialized = false;
        await InitializeAsync();
        RoleAndNodes.InfoMessage = result.IsRunning ? _localizer.Get("Main.RoleApplied") : result.Message;
    }

    /// <summary>
    /// Routes a link state change.
    /// </summary>
    /// <param name="evt">The event.</param>
    private void OnLinkStateChanged(LinkStateChangedEvent evt)
    {
        Overview.ApplyLinkState(evt.State);
        RoleAndNodes.ApplyLinkState(evt.State);
        Tunnel.ApplyLinkState(evt.State);
        if (!evt.State.IsUsable && evt.State.Status == LinkStatus.Failed)
        {
            MarkAttention(TabKey.Tunnel);
        }
    }

    /// <summary>
    /// Routes a patch event.
    /// </summary>
    /// <param name="evt">The event.</param>
    private void OnPatchChanged(PatchEvent evt)
    {
        RefreshOrInvalidate(Sync);
        RefreshOrInvalidate(Overview);
        if (evt.Direction == PatchDirection.Incoming && evt.Status is PatchStatus.ConflictPending or PatchStatus.Failed)
        {
            MarkAttention(TabKey.Sync);
        }
    }

    /// <summary>
    /// Routes a diagnostics event.
    /// </summary>
    /// <param name="evt">The event.</param>
    private void OnDiagnosticsCompleted(DiagnosticsEvent evt)
    {
        Diagnostics.ApplyDiagnostics(evt);
        Overview.ApplyDiagnostics(evt);
        if (!evt.AllPassed)
        {
            MarkAttention(TabKey.Diagnostics);
        }
    }

    /// <summary>
    /// Marks a tab that is not selected.
    /// </summary>
    /// <param name="key">The tab key.</param>
    private void MarkAttention(TabKey key)
    {
        var tab = _allTabs.First(item => item.Key == key);
        if (!tab.IsSelected)
        {
            tab.HasAttention = true;
        }
    }

    /// <summary>
    /// Shows the repository the node switched to: everything the pages hold belongs to the previous one.
    /// </summary>
    /// <param name="added">Whether the repository was just added to this computer, so its current version is still to be pulled.</param>
    private void OnRepositorySwitched(bool added)
    {
        InvalidatePages(except: null);
        if (added && Branches.CanPull)
        {
            // A repository that is not on this computer yet is brought by a Pull into its empty folder.
            Navigate(TabKey.Branches);
            _ = Branches.PullCommand.ExecuteAsync(null);
            return;
        }

        if (_initialized)
        {
            _ = CurrentPage?.RefreshAsync();
        }
    }

    /// <summary>
    /// Reloads a page in the background when it is on screen; otherwise marks it to be reloaded when it is opened.
    /// </summary>
    /// <param name="page">The page whose data changed.</param>
    private void RefreshOrInvalidate(PageViewModelBase page)
    {
        if (_initialized && ReferenceEquals(CurrentPage, page))
        {
            _ = page.RefreshAsync();
        }
        else
        {
            page.Invalidate();
        }
    }

    /// <summary>
    /// Marks the pages to be reloaded when they are opened.
    /// </summary>
    /// <param name="except">The page to leave as it is, or <see langword="null"/> for none.</param>
    private void InvalidatePages(PageViewModelBase? except)
    {
        foreach (var tab in _allTabs)
        {
            if (!ReferenceEquals(tab.Page, except))
            {
                tab.Page.Invalidate();
            }
        }
    }

    /// <summary>
    /// Updates the connection state.
    /// </summary>
    /// <param name="connected">Whether the event stream is connected.</param>
    private void SetConnected(bool connected)
    {
        // Events were missed while the stream was down, so nothing the pages show can be trusted any more.
        if (connected && _eventsLost && _initialized)
        {
            InvalidatePages(except: CurrentPage);
            _ = CurrentPage?.RefreshAsync();
        }

        _eventsLost = !connected && _initialized;
        IsConnected = connected;
        SetConnectionText(connected ? () => _localizer.Get("Main.Connected") : () => _localizer.Get("Main.EventsReconnecting"));
    }
}
