using System.Net;
using System.Net.Sockets;
using RCynQ.Core.Enums;

namespace RCynQ.Sync.Configuration.Probing;

/// <summary>
/// Classifies local addresses for the export host suggestion.
/// </summary>
public static class NetworkAddressClassifier
{
    /// <summary>
    /// Classifies an address.
    /// </summary>
    /// <param name="address">The IP address text.</param>
    /// <param name="isTunnelInterface">Whether the address belongs to a tunnel interface.</param>
    /// <returns>The kind; unparsable text is reported as <see cref="NetworkAddressKind.LinkLocal"/> so it is never suggested.</returns>
    public static NetworkAddressKind Classify(string address, bool isTunnelInterface)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return NetworkAddressKind.LinkLocal;
        }

        if (IPAddress.IsLoopback(ip))
        {
            return NetworkAddressKind.Loopback;
        }

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            if (bytes[0] == 169 && bytes[1] == 254)
            {
                return NetworkAddressKind.LinkLocal;
            }

            if (isTunnelInterface)
            {
                return NetworkAddressKind.Tunnel;
            }

            var isPrivate = bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 100 && bytes[1] is >= 64 and <= 127);
            return isPrivate ? NetworkAddressKind.Private : NetworkAddressKind.Public;
        }

        // Teredo addresses change with the NAT mapping and cannot be given to neighbours.
        if (ip.IsIPv6LinkLocal || ip.IsIPv6Teredo)
        {
            return NetworkAddressKind.LinkLocal;
        }

        if (isTunnelInterface)
        {
            return NetworkAddressKind.Tunnel;
        }

        return ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC ? NetworkAddressKind.Private : NetworkAddressKind.Public;
    }

    /// <summary>
    /// Gets the preference of an address for the export: lower is better.
    /// </summary>
    /// <param name="info">The address.</param>
    /// <returns>The rank.</returns>
    public static int Rank(Core.Api.NetworkAddressInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        var kindRank = info.Kind switch
        {
            NetworkAddressKind.Tunnel => 0,
            NetworkAddressKind.Private => 1,
            NetworkAddressKind.Public => 2,
            _ => 3
        };

        return (info.SshReachable ? 0 : 10) + kindRank;
    }
}
