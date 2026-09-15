using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the runtime status of a transport link.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<LinkStatus>))]
public enum LinkStatus
{
    /// <summary>
    /// The status has not been determined yet.
    /// </summary>
    [Description("Status not determined yet.")]
    Unknown = 1,

    /// <summary>
    /// The link is being established.
    /// </summary>
    [Description("Link is being established.")]
    Starting = 2,

    /// <summary>
    /// The link is established and the peer is reachable.
    /// </summary>
    [Description("Peer is reachable.")]
    Ready = 3,

    /// <summary>
    /// The link works through a fallback transport or with reduced quality.
    /// </summary>
    [Description("Link works through a fallback transport or with reduced quality.")]
    Degraded = 4,

    /// <summary>
    /// The link has been stopped deliberately.
    /// </summary>
    [Description("Link stopped.")]
    Stopped = 5,

    /// <summary>
    /// The link failed to start or has been lost.
    /// </summary>
    [Description("Link failed.")]
    Failed = 6
}
