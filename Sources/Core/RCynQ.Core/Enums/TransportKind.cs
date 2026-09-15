using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the transport mechanism that makes a peer reachable for SSH.
/// </summary>
/// <remarks>
/// A transport only provides reachability; the protected subject channel is always SSH.
/// Only <see cref="Direct"/> and <see cref="AmneziaWG"/> are implemented initially;
/// the other members are reserved so configuration can already name them.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<TransportKind>))]
public enum TransportKind
{
    /// <summary>
    /// The peer is reachable over the existing network without an additional tunnel.
    /// </summary>
    [Description("Existing network without an additional tunnel.")]
    Direct = 1,

    /// <summary>
    /// AmneziaWG (obfuscated WireGuard) tunnel.
    /// </summary>
    [Description("AmneziaWG obfuscated WireGuard tunnel.")]
    AmneziaWG = 2,

    /// <summary>
    /// Plain WireGuard tunnel (reserved for a future transport).
    /// </summary>
    [Description("WireGuard tunnel (future).")]
    WireGuard = 3,

    /// <summary>
    /// OpenConnect VPN (reserved for a future transport).
    /// </summary>
    [Description("OpenConnect VPN (future).")]
    OpenConnect = 4,

    /// <summary>
    /// Tailscale mesh network (reserved for a future transport).
    /// </summary>
    [Description("Tailscale mesh network (future).")]
    Tailscale = 5
}
