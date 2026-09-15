using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// A local network address offered as the host neighbours use to reach this node.
/// </summary>
/// <param name="Address">The IP address.</param>
/// <param name="InterfaceName">The name of the network interface.</param>
/// <param name="Kind">The address kind.</param>
/// <param name="SshReachable">Whether the local SSH server accepted a connection on this address.</param>
public sealed record NetworkAddressInfo(string Address, string InterfaceName, NetworkAddressKind Kind, bool SshReachable);
