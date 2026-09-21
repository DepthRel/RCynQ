using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.Ssh.Routing;

/// <summary>
/// The way to the counterpart endpoint of the chain: which SSH hops to use, and which address and port to talk to.
/// </summary>
/// <remarks>
/// <para>
/// A peer in <see cref="EndpointMode.SshTunnel"/> is reached by opening an SSH session to the peer itself and
/// forwarding to its loopback endpoint; the mediators are jump hosts.
/// </para>
/// <para>
/// A peer in <see cref="EndpointMode.Direct"/> runs no SSH server. Its endpoint listens on the network with TLS, so
/// the SSH session stops at the last mediator and forwards to <see cref="RemoteHost"/>:<see cref="RemotePort"/> from
/// there; TLS is established with the peer itself, so the mediator only relays bytes it cannot read. When there is no
/// mediator at all, no SSH is used: the connection goes straight to the peer.
/// </para>
/// </remarks>
/// <param name="Endpoint">The endpoint link (last link of the chain).</param>
/// <param name="Target">The SSH endpoint that opens the forward, or <see langword="null"/> when no SSH is used.</param>
/// <param name="Jumps">The SSH endpoints of the mediators before the target, in order.</param>
/// <param name="RemoteHost">The host the forward connects to, as seen from <paramref name="Target"/>.</param>
/// <param name="RemotePort">The port the forward connects to.</param>
/// <param name="TlsFingerprint">The pinned certificate fingerprint, or <see langword="null"/> when the endpoint is plain HTTP on loopback.</param>
public sealed record ChainRoute(
    ChainLinkConfig Endpoint,
    SshEndpoint? Target,
    IReadOnlyList<SshEndpoint> Jumps,
    string RemoteHost,
    int RemotePort,
    string? TlsFingerprint)
{
    /// <summary>
    /// Gets a value indicating whether an SSH local forward is needed.
    /// </summary>
    public bool UsesSsh => Target is not null;

    /// <summary>
    /// Gets a value indicating whether the endpoint is reached over TLS with a pinned certificate.
    /// </summary>
    public bool UsesTls => TlsFingerprint is not null;

    /// <summary>
    /// Resolves the route from the chain links.
    /// </summary>
    /// <param name="chain">The chain links; mediators first, the counterpart endpoint last.</param>
    /// <returns>The route.</returns>
    /// <exception cref="ArgumentException">
    /// The chain is empty, or the endpoint runs no SSH server and the mediator that should relay to it runs none either.
    /// </exception>
    public static ChainRoute Resolve(IReadOnlyList<ChainLinkConfig> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("The chain has no links; add a link to the counterpart endpoint first.", nameof(chain));
        }

        var ordered = chain.OrderBy(link => link.Priority).ThenBy(link => link.Name, StringComparer.Ordinal).ToArray();
        var endpoint = ordered[^1];
        var mediators = ordered[..^1];
        if (endpoint.Peer.EndpointMode != EndpointMode.Direct)
        {
            return new ChainRoute(endpoint, endpoint.Peer.Ssh, mediators.Select(link => link.Peer.Ssh).ToArray(), "127.0.0.1", endpoint.RemotePort, null);
        }

        // The endpoint has no SSH server: its own TLS port is the destination.
        if (mediators.Length == 0)
        {
            return new ChainRoute(endpoint, null, Array.Empty<SshEndpoint>(), endpoint.Peer.Host, endpoint.RemotePort, endpoint.Peer.TlsFingerprint);
        }

        var relay = mediators[^1];
        if (relay.Peer.EndpointMode == EndpointMode.Direct)
        {
            throw new ArgumentException(
                Texts.Format("Route.MediatorNeedsSsh", endpoint.Peer.Id, relay.Peer.Id),
                nameof(chain));
        }

        return new ChainRoute(
            endpoint,
            relay.Peer.Ssh,
            mediators[..^1].Select(link => link.Peer.Ssh).ToArray(),
            endpoint.Peer.Host,
            endpoint.RemotePort,
            endpoint.Peer.TlsFingerprint);
    }
}
