using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// The public configuration of a node exchanged manually between nodes (pairing without a key server).
/// </summary>
/// <remarks>
/// Only public material is included: the ECDSA signing public key and optionally the SSH public key. Private keys are
/// never exported.
/// </remarks>
/// <param name="FormatVersion">The file format version; see <see cref="CURRENT_FORMAT_VERSION"/>.</param>
/// <param name="NodeId">The node identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Role">The node role.</param>
/// <param name="SigningPublicKey">The Base64 DER ECDSA P-256 public key, or <see langword="null"/> for a Relay.</param>
/// <param name="Host">The host at which the node's sshd is reachable for its neighbour, or <see langword="null"/>.</param>
/// <param name="SshUser">The SSH user for the neighbour, or <see langword="null"/>.</param>
/// <param name="SshPort">The SSH port.</param>
/// <param name="ListenPort">The loopback port of the node's patch endpoint.</param>
/// <param name="SshPublicKey">The node's SSH public key line for restricted <c>authorized_keys</c> entries, or <see langword="null"/>.</param>
/// <param name="ExportedAtUtc">The export time.</param>
/// <param name="HostKeyFingerprint">
/// The SHA-256 fingerprint of the node's sshd host key, pinned by the importing neighbour, or <see langword="null"/>.
/// </param>
/// <param name="EndpointMode">
/// How the node accepts connections: through its SSH server, or on its own TLS port when no SSH server can be installed.
/// </param>
/// <param name="EndpointPort">The TCP port of the TLS endpoint; meaningful only in <see cref="Enums.EndpointMode.Direct"/>.</param>
/// <param name="TlsFingerprint">
/// The SHA-256 fingerprint of the node's TLS certificate, pinned by the importing neighbour; required in
/// <see cref="Enums.EndpointMode.Direct"/>.
/// </param>
public sealed record ExportedNodeConfiguration(
    int FormatVersion,
    NodeId NodeId,
    string Name,
    NodeRole Role,
    string? SigningPublicKey,
    string? Host,
    string? SshUser,
    int SshPort,
    int ListenPort,
    string? SshPublicKey,
    DateTimeOffset ExportedAtUtc,
    string? HostKeyFingerprint = null,
    EndpointMode EndpointMode = EndpointMode.SshTunnel,
    int EndpointPort = 0,
    string? TlsFingerprint = null)
{
    /// <summary>
    /// The current format version.
    /// </summary>
    public const int CURRENT_FORMAT_VERSION = 1;
}
