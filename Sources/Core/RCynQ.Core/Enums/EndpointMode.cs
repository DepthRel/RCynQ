using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// How neighbours reach the patch endpoint of this node.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EndpointMode>))]
public enum EndpointMode
{
    /// <summary>
    /// The endpoint listens on loopback only and neighbours reach it through an SSH local forward. The default:
    /// it needs no inbound port of its own, and the mediator relays encrypted SSH traffic it cannot read.
    /// </summary>
    SshTunnel = 0,

    /// <summary>
    /// The endpoint also listens on the network over TLS and neighbours connect to it directly, pinning the
    /// certificate fingerprint from the node's export file. For hosts where no SSH server may be installed.
    /// </summary>
    Direct = 1
}
