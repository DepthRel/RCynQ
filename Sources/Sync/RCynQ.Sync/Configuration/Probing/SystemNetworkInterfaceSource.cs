using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// <see cref="INetworkInterfaceSource"/> backed by the operating system network interface table.
/// </summary>
public sealed class SystemNetworkInterfaceSource : INetworkInterfaceSource
{
    /// <summary>
    /// Interface name or description fragments that identify VPN and tunnel adapters.
    /// </summary>
    private static readonly string[] _tunnelHints = { "amnezia", "awg", "wireguard", "wg0", "wg1", "tunnel", "utun", "tun0", "tun1", "tap-", "vpn", "tailscale", "zerotier" };

    /// <inheritdoc />
    public IReadOnlyList<LocalNetworkAddress> GetAddresses()
    {
        var result = new List<LocalNetworkAddress>();
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (NetworkInformationException)
        {
            return result;
        }

        foreach (var nic in interfaces.Where(nic => nic.OperationalStatus == OperationalStatus.Up))
        {
            IPInterfaceProperties properties;
            try
            {
                properties = nic.GetIPProperties();
            }
            catch (NetworkInformationException)
            {
                continue;
            }

            var tunnel = IsTunnel(nic.NetworkInterfaceType, nic.Name, nic.Description);
            foreach (var unicast in properties.UnicastAddresses)
            {
                if (unicast.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                {
                    result.Add(new LocalNetworkAddress(unicast.Address.ToString(), nic.Name, tunnel));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Decides whether an interface is a VPN or tunnel interface.
    /// </summary>
    /// <param name="type">The interface type.</param>
    /// <param name="name">The interface name.</param>
    /// <param name="description">The interface description.</param>
    /// <returns><see langword="true"/> for tunnel interfaces.</returns>
    public static bool IsTunnel(NetworkInterfaceType type, string? name, string? description)
    {
        if (type is NetworkInterfaceType.Tunnel or NetworkInterfaceType.Ppp)
        {
            return true;
        }

        var text = $"{name} {description}";
        return _tunnelHints.Any(hint => text.Contains(hint, StringComparison.OrdinalIgnoreCase));
    }
}
