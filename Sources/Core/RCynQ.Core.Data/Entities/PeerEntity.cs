using RCynQ.Core.Enums;

namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a known peer (table <c>Peers</c>).
/// </summary>
public sealed class PeerEntity
{
    /// <summary>Gets or sets the peer node identifier (primary key).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the peer role as its integer value.</summary>
    public int Role { get; set; }

    /// <summary>Gets or sets the host name or address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the SSH port.</summary>
    public int SshPort { get; set; }

    /// <summary>Gets or sets the SSH user.</summary>
    public string SshUser { get; set; } = string.Empty;

    /// <summary>Gets or sets the expected host key fingerprint.</summary>
    public string? HostKeyFingerprint { get; set; }

    /// <summary>Gets or sets the Base64 DER signing public key.</summary>
    public string? SigningPublicKey { get; set; }

    /// <summary>Gets or sets the repository path on a Dest peer.</summary>
    public string? GitRepoPath { get; set; }

    /// <summary>Gets or sets the OpenSSH public key line of the peer.</summary>
    public string? SshPublicKey { get; set; }

    /// <summary>Gets or sets how the peer accepts connections.</summary>
    public EndpointMode EndpointMode { get; set; } = EndpointMode.SshTunnel;

    /// <summary>Gets or sets the pinned SHA-256 fingerprint of the peer's TLS certificate.</summary>
    public string? TlsFingerprint { get; set; }

    /// <summary>Gets or sets the TCP port of the peer's TLS endpoint; <c>0</c> when it uses its SSH server.</summary>
    public int EndpointPort { get; set; }

    /// <summary>Gets or sets the time the row was last updated.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
