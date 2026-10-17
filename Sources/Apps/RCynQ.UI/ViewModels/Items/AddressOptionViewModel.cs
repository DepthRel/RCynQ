using CommunityToolkit.Mvvm.ComponentModel;
using RCynQ.Core.Api;
using RCynQ.Localization;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A detected local address offered in the export dialog; every one is exported unless the user leaves it out.
/// </summary>
public sealed partial class AddressOptionViewModel : ObservableObject
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>Whether the address goes into the export.</summary>
    [ObservableProperty]
    private bool _isIncluded = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddressOptionViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="info">The detected address.</param>
    /// <param name="checkedOnOwnPort">
    /// <see langword="true"/> when reachability was checked on the node's own RCynQ port rather than on its SSH server; it only changes
    /// the wording of <see cref="StatusText"/>.
    /// </param>
    public AddressOptionViewModel(ILocalizer localizer, NetworkAddressInfo info, bool checkedOnOwnPort = false)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        Info = info ?? throw new ArgumentNullException(nameof(info));
        _checkedOnOwnPort = checkedOnOwnPort;
    }

    /// <summary>Whether reachability was checked on the node's TLS port rather than on its SSH server.</summary>
    private readonly bool _checkedOnOwnPort;

    /// <summary>Gets the detected address.</summary>
    public NetworkAddressInfo Info { get; }

    /// <summary>Gets the IP address.</summary>
    public string Address => Info.Address;

    /// <summary>Gets the interface name.</summary>
    public string InterfaceName => Info.InterfaceName;

    /// <summary>Gets the address kind label.</summary>
    public string KindText => DisplayText.AddressKind(_localizer, Info.Kind);

    /// <summary>Gets a value indicating whether SSH answered on the address.</summary>
    public bool SshReachable => Info.SshReachable;

    /// <summary>Gets the SSH status label.</summary>
    public string StatusText => _localizer.Get($"Address.Status.{_checkedOnOwnPort}.{Info.SshReachable}");
}
