using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// A stage of joining a node with an invitation, in the order they run.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<JoinStep>))]
public enum JoinStep
{
    /// <summary>
    /// The invitation is read and checked: format, role, protocol version, expiry.
    /// </summary>
    Invitation = 0,

    /// <summary>
    /// The inviting node and its Relays are added to the chain of this node.
    /// </summary>
    Nodes = 1,

    /// <summary>
    /// This node enrolls at the Relay on the way.
    /// </summary>
    Relay = 2,

    /// <summary>
    /// This node enrolls at the inviting node.
    /// </summary>
    Inviter = 3,

    /// <summary>
    /// The link token is stored and the neighbours are allowed in.
    /// </summary>
    Access = 4
}
