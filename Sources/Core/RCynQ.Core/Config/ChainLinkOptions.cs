using RCynQ.Core.Enums;

namespace RCynQ.Core.Config;

/// <summary>
/// Bindable chain link settings.
/// </summary>
public sealed class ChainLinkOptions
{
    /// <summary>
    /// Gets or sets the link identifier. When empty a stable identifier is derived from <see cref="Name"/>.
    /// </summary>
    public Guid? Id { get; set; }

    /// <summary>
    /// Gets or sets the link display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the primary transport.
    /// </summary>
    public TransportKind Transport { get; set; } = TransportKind.Direct;

    /// <summary>
    /// Gets or sets the fallback transports, tried in order.
    /// </summary>
    public List<TransportKind> FallbackTransports { get; set; } = new();

    /// <summary>
    /// Gets or sets transport specific settings.
    /// </summary>
    public Dictionary<string, string> TransportSettings { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets the peer node identifier.
    /// </summary>
    public string PeerNodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the peer display name. Defaults to <see cref="PeerNodeId"/>.
    /// </summary>
    public string? PeerName { get; set; }

    /// <summary>
    /// Gets or sets the peer role.
    /// </summary>
    public NodeRole PeerRole { get; set; } = NodeRole.Dest;

    /// <summary>
    /// Gets or sets the peer host.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the peer SSH port.
    /// </summary>
    public int SshPort { get; set; } = 22;

    /// <summary>
    /// Gets or sets the SSH user on the peer.
    /// </summary>
    public string SshUser { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the expected SSH host key fingerprint.
    /// </summary>
    public string? HostKeyFingerprint { get; set; }

    /// <summary>
    /// Gets or sets the Base64 DER signing public key of the peer.
    /// </summary>
    public string? SigningPublicKey { get; set; }

    /// <summary>
    /// Gets or sets the repository path on a Dest peer.
    /// </summary>
    public string? GitRepoPath { get; set; }

    /// <summary>
    /// Gets or sets the local forward port (0 = ephemeral).
    /// </summary>
    public int LocalPort { get; set; }

    /// <summary>
    /// Gets or sets the remote port (peer patch endpoint for the endpoint link, next hop SSH port for mediators).
    /// </summary>
    public int RemotePort { get; set; } = 18443;

    /// <summary>
    /// Gets or sets the chain position.
    /// </summary>
    public int Priority { get; set; }
}
