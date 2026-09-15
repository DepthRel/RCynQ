using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Dispatching;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels.Formatting;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
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

    /// <summary>The data directory checked for a copied installation, or <see langword="null"/>.</summary>
    private string? _dataRoot;

    /// <summary>The selected tab.</summary>
    [ObservableProperty]
    private NavigationItemViewModel? _selectedTab;

    /// <summary>Whether the live event stream is connected.</summary>
    [ObservableProperty]
    private bool _isConnected;

    /// <summary>The connection status text.</summary>
    [ObservableProperty]
    private string _connectionText = "Подключение к сервису…";

    /// <summary>The service status text.</summary>
    [ObservableProperty]
    private string _serviceText = string.Empty;

    /// <summary>The node title shown in the window title.</summary>
    [ObservableProperty]
    private string _nodeTitle = "RCynQ";

    /// <summary>Whether the first-run setup is shown instead of the tabs.</summary>
    [ObservableProperty]
    private bool _isSetupVisible;

    /// <summary>The problem that prevents using the service, shown once instead of per-page errors.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStartupError))]
    private string? _startupError;

    /// <summary>The warning shown when the installation was copied from another computer together with its data.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInstallationWarning))]
    private string? _installationWarning;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
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
    public MainViewModel(
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
        LogsViewModel logs)
    {
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

        _allTabs = new List<NavigationItemViewModel>
        {
            new(TabKey.Overview, "Обзор", "Icon.Home", overview),
            new(TabKey.RoleAndNodes, "Роль и узлы", "Icon.Nodes", roleAndNodes),
            new(TabKey.Sync, "Синхронизация", "Icon.Upload", sync),
            new(TabKey.Tunnel, "Туннель", "Icon.Tunnel", tunnel),
            new(TabKey.Diagnostics, "Диагностика", "Icon.Stethoscope", diagnostics),
            new(TabKey.Branches, "Ветки", "Icon.Branch", branches),
            new(TabKey.Logs, "Логи", "Icon.Logs", logs)
        };

        foreach (var tab in _allTabs)
        {
            Tabs.Add(tab);
        }

        overview.NavigationRequested += (_, key) => Navigate(key);
        roleAndNodes.RestartRequested += (_, _) => _ = RestartServiceAsync();
        setup.Completed += (_, _) => _ = CompleteSetupAsync();
        _controller.StatusChanged += (_, status) => _dispatcher.Post(() => ServiceText = FormatService(status));
        _events.LinkStateChanged += (_, evt) => _dispatcher.Post(() => OnLinkStateChanged(evt));
        _events.PatchChanged += (_, evt) => _dispatcher.Post(() => OnPatchChanged(evt));
        _events.DiagnosticsCompleted += (_, evt) => _dispatcher.Post(() => OnDiagnosticsCompleted(evt));
        _events.ConnectionChanged += (_, connected) => _dispatcher.Post(() => SetConnected(connected));
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

        ConnectionText = "Запуск сервиса RCynQ…";
        var start = await _controller.EnsureRunningAsync();
        if (start.IsConfigurationError)
        {
            ShowSetup(start.Message);
            return;
        }

        IsSetupVisible = false;
        CheckInstallation();
        var role = NodeRole.Source;
        try
        {
            var config = await _api.GetConfigAsync();
            role = config.Role;
            NodeTitle = $"RCynQ — {config.Name} ({config.Role})";
            Branches.Configure(config.Capabilities);
            ConnectionText = start.Message;
            StartupError = null;
            _initialized = true;
        }
        catch (Exception ex) when (ex is ServiceApiException or HttpRequestException)
        {
            _initialized = false;
            ConnectionText = start.IsRunning ? $"Нет связи с сервисом: {ex.Message}" : start.Message;
            StartupError = ConnectionText;
        }

        var tab = _roleAdapter.Apply(_allTabs, role);
        if (ReferenceEquals(tab, SelectedTab))
        {
            if (_initialized)
            {
                _ = tab?.Page.RefreshAsync();
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
                _ = newValue.Page.RefreshAsync();
            }
        }

        OnPropertyChanged(nameof(CurrentPage));
    }

    /// <summary>
    /// Formats the service status for the status bar.
    /// </summary>
    /// <param name="status">The status.</param>
    /// <returns>The text.</returns>
    private static string FormatService(ServiceControllerStatus status)
    {
        var text = DisplayText.Service(status);
        return text.Length == 0 ? string.Empty : "· " + text;
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
            await CurrentPage.RefreshAsync();
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
        InstallationWarning = check.CopiedFromAnotherMachine
            ? $"Папка программы скопирована с компьютера «{check.RecordedMachineName}» вместе с его настройками и ключами. "
              + "Если это новый узел, закройте программу, удалите папки «config» и «data» и запустите её снова. "
              + "Если вы перенесли этот же узел на новый компьютер, нажмите «Это тот же узел»."
            : null;
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

        InstallationWarning = null;
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
        NodeTitle = "RCynQ — настройка узла";
        ConnectionText = error is null ? "Требуется настройка узла" : "Сервис не принял конфигурацию";
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
            ConnectionText = "Перезапуск сервиса…";
            var restart = await _controller.RestartAsync();
            if (restart.IsConfigurationError)
            {
                ShowSetup(restart.Message);
                return;
            }
        }

        _initialized = false;
        await InitializeAsync();
    }

    /// <summary>
    /// Restarts the service after a saved role change and adapts the tabs to the new role.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    private async Task RestartServiceAsync()
    {
        ConnectionText = "Перезапуск сервиса с новой конфигурацией…";
        var result = await _controller.RestartAsync();
        if (result.Outcome == ServiceStartOutcome.NotManaged)
        {
            ConnectionText = result.Message;
            RoleAndNodes.InfoMessage = "Роль сохранена. " + result.Message;
            return;
        }

        if (result.IsConfigurationError)
        {
            ShowSetup(result.Message);
            return;
        }

        _initialized = false;
        await InitializeAsync();
        RoleAndNodes.InfoMessage = result.IsRunning ? "Роль применена: сервис перезапущен." : result.Message;
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
        _ = Sync.RefreshAsync();
        _ = Overview.RefreshAsync();
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
    /// Updates the connection state.
    /// </summary>
    /// <param name="connected">Whether the event stream is connected.</param>
    private void SetConnected(bool connected)
    {
        IsConnected = connected;
        ConnectionText = connected ? "Подключено к сервису" : "Нет подписки на события сервиса, повторное подключение…";
    }
}
