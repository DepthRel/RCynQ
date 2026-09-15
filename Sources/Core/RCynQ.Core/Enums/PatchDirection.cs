using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines whether a patch was produced locally or received from the peer.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PatchDirection>))]
public enum PatchDirection
{
    /// <summary>
    /// The patch was built on this node and sent to the peer.
    /// </summary>
    [Description("Built locally and sent to the peer.")]
    Outgoing = 1,

    /// <summary>
    /// The patch was received from the peer.
    /// </summary>
    [Description("Received from the peer.")]
    Incoming = 2
}
