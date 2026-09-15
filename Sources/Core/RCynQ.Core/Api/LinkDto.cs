using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// A chain link as exposed by the API. Signing keys are reduced to a flag.
/// </summary>
/// <param name="Id">The link identifier.</param>
/// <param name="Name">The link name.</param>
/// <param name="Transport">The primary transport.</param>
/// <param name="FallbackTransports">The fallback transports.</param>
/// <param name="TransportSettings">Transport specific settings.</param>
/// <param name="PeerNodeId">The peer identifier.</param>
/// <param name="PeerName">The peer display name.</param>
/// <param name="PeerRole">The peer role.</param>
/// <param name="Host">The peer host.</param>
/// <param name="SshPort">The peer SSH port.</param>
/// <param name="SshUser">The SSH user.</param>
/// <param name="HostKeyFingerprint">The expected host key fingerprint.</param>
/// <param name="HasSigningKey">Whether a signing public key of the peer is known.</param>
/// <param name="GitRepoPath">The repository path on a Dest peer.</param>
/// <param name="LocalPort">The local forward port.</param>
/// <param name="RemotePort">The remote port.</param>
/// <param name="Priority">The chain position.</param>
public sealed record LinkDto(
    Guid Id,
    string Name,
    TransportKind Transport,
    IReadOnlyList<TransportKind> FallbackTransports,
    IReadOnlyDictionary<string, string> TransportSettings,
    NodeId PeerNodeId,
    string PeerName,
    NodeRole PeerRole,
    string Host,
    int SshPort,
    string SshUser,
    string? HostKeyFingerprint,
    bool HasSigningKey,
    string? GitRepoPath,
    int LocalPort,
    int RemotePort,
    int Priority)
{
    /// <summary>
    /// Creates the DTO of a link configuration.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The DTO.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="link"/> is <see langword="null"/>.</exception>
    public static LinkDto FromConfig(ChainLinkConfig link)
    {
        ArgumentNullException.ThrowIfNull(link);
        var peer = link.Peer;
        return new LinkDto(
            link.Id, link.Name, link.Transport, link.FallbackTransports, link.TransportSettings,
            peer.Id, peer.Name, peer.Role, peer.Host, peer.SshPort, peer.SshUser, peer.HostKeyFingerprint,
            peer.SigningPublicKey is not null, peer.GitRepoPath, link.LocalPort, link.RemotePort, link.Priority);
    }
}
