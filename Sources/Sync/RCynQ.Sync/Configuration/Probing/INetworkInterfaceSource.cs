namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// Lists the addresses of the local network interfaces.
/// </summary>
public interface INetworkInterfaceSource
{
    /// <summary>
    /// Gets the IPv4 and IPv6 unicast addresses of all operational interfaces.
    /// </summary>
    /// <returns>The addresses.</returns>
    IReadOnlyList<LocalNetworkAddress> GetAddresses();
}
