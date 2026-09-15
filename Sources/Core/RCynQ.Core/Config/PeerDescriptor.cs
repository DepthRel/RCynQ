using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Config;

/// <summary>
/// Describes a remote node that this node communicates with.
/// </summary>
/// <remarks>
/// A peer is either the counterpart endpoint (Source or Dest) or a Mediator used as SSH jump host.
/// Peer data is exchanged manually via export/import; there is no central key server.
/// </remarks>
public sealed record PeerDescriptor
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PeerDescriptor"/> record.
    /// </summary>
    /// <param name="id">The peer node identifier.</param>
    /// <param name="name">The display name of the peer.</param>
    /// <param name="role">The role of the peer.</param>
    /// <param name="host">The host name or IP address at which the peer's SSH server is reachable.</param>
    /// <param name="sshPort">The SSH server port of the peer.</param>
    /// <param name="sshUser">The user name used to log in to the peer over SSH.</param>
    /// <param name="hostKeyFingerprint">
    /// The expected SSH host key fingerprint (<c>SHA256:...</c>), or <see langword="null"/> to rely on
    /// <c>StrictHostKeyChecking=accept-new</c>.
    /// </param>
    /// <param name="signingPublicKey">
    /// The Base64 DER (SubjectPublicKeyInfo) ECDSA P-256 public key used to verify patches from this peer,
    /// or <see langword="null"/> when the peer does not send patches (Mediator).
    /// </param>
    /// <param name="gitRepoPath">
    /// The absolute repository path on a Dest peer used for git-over-ssh, or <see langword="null"/>.
    /// </param>
    /// <param name="sshPublicKey">
    /// The OpenSSH public key line of the peer, allowed in the local <c>authorized_keys</c>, or <see langword="null"/>.
    /// </param>
    /// <exception cref="ArgumentException">A required value is blank, invalid or the identifier is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sshPort"/> is outside 1..65535.</exception>
    public PeerDescriptor(
        NodeId id,
        string name,
        NodeRole role,
        string host,
        int sshPort,
        string sshUser,
        string? hostKeyFingerprint = null,
        string? signingPublicKey = null,
        string? gitRepoPath = null,
        string? sshPublicKey = null)
    {
        SshPublicKey = Guard.OptionalNotBlank(sshPublicKey, nameof(sshPublicKey));
        if (id.IsEmpty)
        {
            throw new ArgumentException("Peer identifier must not be empty.", nameof(id));
        }

        Id = id;
        Name = Guard.NotBlank(name, nameof(name));
        Role = Guard.Defined(role, nameof(role));
        Ssh = new SshEndpoint(sshUser, host, sshPort);
        HostKeyFingerprint = Guard.OptionalNotBlank(hostKeyFingerprint, nameof(hostKeyFingerprint));
        SigningPublicKey = ValidateBase64(signingPublicKey, nameof(signingPublicKey));
        GitRepoPath = Guard.OptionalNotBlank(gitRepoPath, nameof(gitRepoPath));
    }

    /// <summary>
    /// Gets the peer node identifier.
    /// </summary>
    public NodeId Id { get; }

    /// <summary>
    /// Gets the OpenSSH public key line of the peer, or <see langword="null"/>.
    /// </summary>
    public string? SshPublicKey { get; }

    /// <summary>
    /// Gets the display name of the peer.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the role of the peer.
    /// </summary>
    public NodeRole Role { get; }

    /// <summary>
    /// Gets the SSH endpoint of the peer.
    /// </summary>
    public SshEndpoint Ssh { get; }

    /// <summary>
    /// Gets the host name or IP address of the peer.
    /// </summary>
    public string Host => Ssh.Host;

    /// <summary>
    /// Gets the SSH port of the peer.
    /// </summary>
    public int SshPort => Ssh.Port;

    /// <summary>
    /// Gets the SSH user name for the peer.
    /// </summary>
    public string SshUser => Ssh.User;

    /// <summary>
    /// Gets the expected SSH host key fingerprint, or <see langword="null"/>.
    /// </summary>
    public string? HostKeyFingerprint { get; }

    /// <summary>
    /// Gets the Base64 DER ECDSA public key used to verify patches from this peer, or <see langword="null"/>.
    /// </summary>
    public string? SigningPublicKey { get; }

    /// <summary>
    /// Gets the absolute repository path on a Dest peer, or <see langword="null"/>.
    /// </summary>
    public string? GitRepoPath { get; }

    /// <summary>
    /// Validates that an optional value is well-formed Base64.
    /// </summary>
    /// <param name="value">The candidate value.</param>
    /// <param name="parameterName">The parameter name used in the exception.</param>
    /// <returns>The trimmed value or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException">The value is not valid Base64.</exception>
    private static string? ValidateBase64(string? value, string parameterName)
    {
        var trimmed = Guard.OptionalNotBlank(value, parameterName);
        if (trimmed is null)
        {
            return null;
        }

        var buffer = new byte[trimmed.Length];
        if (!Convert.TryFromBase64String(trimmed, buffer, out _))
        {
            throw new ArgumentException("Signing public key must be Base64 encoded.", parameterName);
        }

        return trimmed;
    }
}
