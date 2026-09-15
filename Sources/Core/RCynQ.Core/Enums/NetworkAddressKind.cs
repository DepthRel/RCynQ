using System.ComponentModel;

namespace RCynQ.Core.Enums;

/// <summary>
/// The kind of a local network address found while preparing an export.
/// </summary>
public enum NetworkAddressKind
{
    /// <summary>
    /// A loopback address (127.0.0.0/8, ::1); never usable by neighbours.
    /// </summary>
    [Description("Loopback address.")]
    Loopback = 1,

    /// <summary>
    /// A link-local address (169.254.0.0/16, fe80::/10).
    /// </summary>
    [Description("Link-local address.")]
    LinkLocal = 2,

    /// <summary>
    /// A private or carrier-grade NAT address (10/8, 172.16/12, 192.168/16, 100.64/10, fc00::/7).
    /// </summary>
    [Description("Private network address.")]
    Private = 3,

    /// <summary>
    /// An address of a VPN or tunnel interface (AmneziaWG, WireGuard and similar).
    /// </summary>
    [Description("VPN or tunnel interface address.")]
    Tunnel = 4,

    /// <summary>
    /// A public address.
    /// </summary>
    [Description("Public address.")]
    Public = 5
}
