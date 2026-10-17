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
/// <param name="Addresses">
/// Every address the node is reachable at, the preferred one first; <see cref="Host"/> repeats the first one for nodes
/// of 0.1.0. <see langword="null"/> in a card of 0.1.0.
/// </param>
/// <param name="ProtocolVersion">
/// The <see cref="CURRENT_PROTOCOL_VERSION"/> of the exporting node, or <see langword="null"/> for a node of 0.1.0.
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
    string? TlsFingerprint = null,
    IReadOnlyList<string>? Addresses = null,
    int? ProtocolVersion = null)
{
    /// <summary>
    /// The current format version. It stays 1 while new fields are optional: a node of 0.1.0 refuses any other
    /// version but ignores fields it does not know.
    /// </summary>
    public const int CURRENT_FORMAT_VERSION = 1;

    /// <summary>
    /// The version of the exchange between nodes; it rises when nodes of different versions can no longer talk.
    /// </summary>
    public const int CURRENT_PROTOCOL_VERSION = 1;

    /// <summary>
    /// The most addresses one card may list.
    /// </summary>
    public const int MAX_ADDRESSES = 16;

    /// <summary>
    /// The addresses the node is reachable at, the preferred one first: the list of the card, or its host alone in a
    /// card of 0.1.0.
    /// </summary>
    /// <returns>The addresses; empty when the card names none.</returns>
    public IReadOnlyList<string> ReachableAt()
        => Addresses is { Count: > 0 }
            ? Addresses
            : Host is null ? Array.Empty<string>() : new[] { Host };
}
