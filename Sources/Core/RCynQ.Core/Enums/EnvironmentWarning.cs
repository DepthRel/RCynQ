using System.ComponentModel;

namespace RCynQ.Core.Enums;

/// <summary>
/// A problem found while probing the node environment for an export.
/// </summary>
public enum EnvironmentWarning
{
    /// <summary>
    /// The local SSH server does not accept connections.
    /// </summary>
    [Description("The local sshd does not accept connections.")]
    SshdNotListening = 1,

    /// <summary>
    /// No local address accepts SSH connections.
    /// </summary>
    [Description("No local address accepts SSH connections.")]
    NoReachableAddress = 2,

    /// <summary>
    /// No SSH public key was found for this node.
    /// </summary>
    [Description("No SSH public key was found.")]
    NoSshPublicKey = 3,

    /// <summary>
    /// The local RCynQ service port does not accept connections.
    /// </summary>
    [Description("The service port does not accept connections.")]
    ServicePortNotListening = 4,

    /// <summary>
    /// The host keys of the local SSH server could not be read.
    /// </summary>
    [Description("The sshd host keys could not be read.")]
    HostKeysUnavailable = 5,

    /// <summary>
    /// The node accepts connections on its own TLS port, but no local address answers on it.
    /// </summary>
    [Description("No local address accepts connections on the node's TLS port.")]
    EndpointNotListening = 6,

    /// <summary>
    /// The node runs under an administrator account of Windows, whose <c>authorized_keys</c> only an administrator may
    /// change: every neighbour that joins or is allowed waits for the administrator prompt on this computer.
    /// </summary>
    [Description("The authorized_keys file of this account needs administrator rights to change.")]
    AccessNeedsAdministrator = 7
}
