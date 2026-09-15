using RCynQ.Core.Api;
using RCynQ.UI.ViewModels.Formatting;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// A detected local address offered in the export dialog.
/// </summary>
public sealed class AddressOptionViewModel
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AddressOptionViewModel"/> class.
    /// </summary>
    /// <param name="info">The detected address.</param>
    public AddressOptionViewModel(NetworkAddressInfo info)
    {
        Info = info ?? throw new ArgumentNullException(nameof(info));
    }

    /// <summary>Gets the detected address.</summary>
    public NetworkAddressInfo Info { get; }

    /// <summary>Gets the IP address.</summary>
    public string Address => Info.Address;

    /// <summary>Gets the interface name.</summary>
    public string InterfaceName => Info.InterfaceName;

    /// <summary>Gets the address kind label.</summary>
    public string KindText => DisplayText.AddressKind(Info.Kind);

    /// <summary>Gets a value indicating whether SSH answered on the address.</summary>
    public bool SshReachable => Info.SshReachable;

    /// <summary>Gets the SSH status label.</summary>
    public string StatusText => Info.SshReachable ? "SSH отвечает" : "SSH не отвечает";
}
