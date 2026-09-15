using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.UI.ViewModels.Dialogs;

/// <summary>
/// The dialog that adds a node (chain link) manually.
/// </summary>
public sealed partial class AddNodeDialogViewModel : DialogViewModelBase
{
    /// <summary>The peer node identifier.</summary>
    [ObservableProperty]
    private string _nodeId = string.Empty;

    /// <summary>The peer role.</summary>
    [ObservableProperty]
    private NodeRole _role = NodeRole.Mediator;

    /// <summary>The peer host.</summary>
    [ObservableProperty]
    private string _host = string.Empty;

    /// <summary>The SSH user.</summary>
    [ObservableProperty]
    private string _sshUser = string.Empty;

    /// <summary>The SSH port.</summary>
    [ObservableProperty]
    private decimal _sshPort = 22;

    /// <summary>The remote port.</summary>
    [ObservableProperty]
    private decimal _remotePort = 22;

    /// <summary>The chain position.</summary>
    [ObservableProperty]
    private decimal _priority = 10;

    /// <summary>The transport.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsConfigPath))]
    private TransportKind _transport = TransportKind.Direct;

    /// <summary>The AmneziaWG configuration path.</summary>
    [ObservableProperty]
    private string _configPath = string.Empty;

    /// <summary>The validation error.</summary>
    [ObservableProperty]
    private string? _error;

    /// <summary>Gets the selectable roles.</summary>
    public IReadOnlyList<NodeRole> Roles { get; } = new[] { NodeRole.Source, NodeRole.Mediator, NodeRole.Dest };

    /// <summary>Gets the selectable transports.</summary>
    public IReadOnlyList<TransportKind> Transports { get; } = new[] { TransportKind.Direct, TransportKind.AmneziaWG };

    /// <summary>Gets a value indicating whether the AmneziaWG configuration path is needed.</summary>
    public bool NeedsConfigPath => Transport == TransportKind.AmneziaWG;

    /// <summary>
    /// Builds the link settings from the input.
    /// </summary>
    /// <returns>The link settings.</returns>
    public ChainLinkOptions ToOptions()
    {
        var options = new ChainLinkOptions
        {
            Name = NodeId.Trim(),
            PeerNodeId = NodeId.Trim(),
            PeerRole = Role,
            Host = Host.Trim(),
            SshUser = SshUser.Trim(),
            SshPort = (int)SshPort,
            RemotePort = (int)RemotePort,
            Priority = (int)Priority,
            Transport = Transport
        };

        if (NeedsConfigPath && !string.IsNullOrWhiteSpace(ConfigPath))
        {
            options.TransportSettings["configPath"] = ConfigPath.Trim();
        }

        return options;
    }

    /// <summary>
    /// Adjusts the default remote port when the role changes.
    /// </summary>
    /// <param name="value">The new role.</param>
    partial void OnRoleChanged(NodeRole value)
    {
        RemotePort = value == NodeRole.Mediator ? 22 : 18443;
        Priority = value == NodeRole.Mediator ? 10 : 1000;
    }

    /// <summary>
    /// Validates the input and confirms the dialog.
    /// </summary>
    [RelayCommand]
    private void Confirm()
    {
        try
        {
            if (!Core.Ids.NodeId.TryParse(NodeId.Trim(), out _))
            {
                throw new ArgumentException("Недопустимый идентификатор узла.");
            }

            SyncBridgeOptionsValidator.ToChainLink(ToOptions());
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Error = ex.Message;
            return;
        }

        Error = null;
        Close(true);
    }

    /// <summary>
    /// Cancels the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel() => Close(false);
}
