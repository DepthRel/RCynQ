using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Localization;
using RCynQ.Ssh.Server;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// The buttons that make this computer accept neighbours when it does not yet: install the SSH server, or open the TLS
/// port in the firewall, with the outcome and the commands to run by hand. The export dialog and the connect wizard
/// show it.
/// </summary>
public sealed partial class ServerSetupPanelViewModel : ObservableObject
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Installs the SSH server or opens the TLS port, or <see langword="null"/> when it is not offered.</summary>
    private readonly IServerSetup? _serverSetup;

    /// <summary>Checks the computer again after a successful change.</summary>
    private readonly Func<Task> _recheck;

    /// <summary>Whether the node accepts connections on its own TLS port rather than through its SSH server.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSshServer), nameof(CanOpenPort))]
    private bool _isDirectEndpoint;

    /// <summary>Whether the SSH server or the TLS port answers.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSshServer), nameof(CanOpenPort))]
    private bool _isReachable = true;

    /// <summary>Whether the owner checks the computer at the moment.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSshServer), nameof(CanOpenPort))]
    private bool _isChecking;

    /// <summary>The TLS port of the node.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OpenPortText))]
    private int _endpointPort;

    /// <summary>Whether the SSH server is being installed or the port opened.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstallSshServer), nameof(CanOpenPort))]
    private bool _isSettingUp;

    /// <summary>The message of the last change, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSetupMessage))]
    private string? _setupMessage;

    /// <summary>Whether the last change left the computer ready.</summary>
    [ObservableProperty]
    private bool _isSetupReady;

    /// <summary>The commands to run by hand after a failed change, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasManualCommands))]
    private string? _manualCommands;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSetupPanelViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="serverSetup">Installs the SSH server or opens the TLS port, or <see langword="null"/> when it is not offered.</param>
    /// <param name="recheck">Checks the computer again after a successful change.</param>
    public ServerSetupPanelViewModel(ILocalizer localizer, IServerSetup? serverSetup, Func<Task> recheck)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _serverSetup = serverSetup;
        _recheck = recheck ?? throw new ArgumentNullException(nameof(recheck));
    }

    /// <summary>Gets a value indicating whether installing the SSH server is offered: it does not answer.</summary>
    public bool CanInstallSshServer => _serverSetup is not null && !IsDirectEndpoint && !IsReachable && !IsChecking && !IsSettingUp;

    /// <summary>Gets a value indicating whether opening the TLS port is offered: it does not answer.</summary>
    public bool CanOpenPort => _serverSetup is not null && IsDirectEndpoint && !IsReachable && !IsChecking && !IsSettingUp;

    /// <summary>Gets the label of the button that opens the TLS port.</summary>
    public string OpenPortText => _localizer.Format("ServerSetup.OpenPort", EndpointPort);

    /// <summary>Gets a value indicating whether a change left a message.</summary>
    public bool HasSetupMessage => !string.IsNullOrEmpty(SetupMessage);

    /// <summary>Gets a value indicating whether there are commands to run by hand.</summary>
    public bool HasManualCommands => !string.IsNullOrEmpty(ManualCommands);

    /// <summary>
    /// Takes what a check of the computer found.
    /// </summary>
    /// <param name="directEndpoint">Whether the node uses its own TLS port.</param>
    /// <param name="reachable">Whether the SSH server or the TLS port answers.</param>
    /// <param name="endpointPort">The TLS port.</param>
    public void Apply(bool directEndpoint, bool reachable, int endpointPort)
    {
        IsDirectEndpoint = directEndpoint;
        IsReachable = reachable;
        EndpointPort = endpointPort;
    }

    /// <summary>
    /// Installs, starts and opens the SSH server of this computer, then checks it again.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task InstallSshServerAsync() => SetUpAsync(setup => setup.InstallSshServerAsync());

    /// <summary>
    /// Lets the TLS port of the node through the firewall, then checks the computer again.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    [RelayCommand]
    private Task OpenPortAsync() => SetUpAsync(setup => setup.OpenEndpointPortAsync(EndpointPort));

    /// <summary>
    /// Runs a change that needs administrator rights, shows how it ended and, when the computer is ready, checks it again.
    /// </summary>
    /// <param name="change">The change.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task SetUpAsync(Func<IServerSetup, Task<ServerSetupResult>> change)
    {
        if (_serverSetup is null)
        {
            return;
        }

        IsSettingUp = true;
        SetupMessage = _localizer.Get("ServerSetup.Running");
        IsSetupReady = false;
        ManualCommands = null;
        ServerSetupResult result;
        try
        {
            result = await change(_serverSetup);
        }
        finally
        {
            IsSettingUp = false;
        }

        SetupMessage = result.Message;
        IsSetupReady = result.IsReady;
        ManualCommands = result.ManualCommands;
        if (result.IsReady)
        {
            await _recheck();
        }
    }
}
