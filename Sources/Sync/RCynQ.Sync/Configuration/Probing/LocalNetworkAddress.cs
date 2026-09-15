namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// An IP address assigned to an operational local network interface.
/// </summary>
/// <param name="Address">The IP address text.</param>
/// <param name="InterfaceName">The interface name.</param>
/// <param name="IsTunnelInterface">Whether the interface is a VPN or tunnel interface.</param>
public sealed record LocalNetworkAddress(string Address, string InterfaceName, bool IsTunnelInterface);
