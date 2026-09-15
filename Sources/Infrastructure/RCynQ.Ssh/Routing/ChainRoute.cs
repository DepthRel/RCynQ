using RCynQ.Core.Config;
using RCynQ.Core.Validation;

namespace RCynQ.Ssh.Routing;

/// <summary>
/// A resolved route through the chain: jump hosts followed by the endpoint.
/// </summary>
/// <param name="Endpoint">The endpoint link (last link of the chain).</param>
/// <param name="Target">The SSH endpoint of the endpoint peer.</param>
/// <param name="Jumps">The SSH endpoints of the mediators, in order.</param>
public sealed record ChainRoute(ChainLinkConfig Endpoint, SshEndpoint Target, IReadOnlyList<SshEndpoint> Jumps)
{
    /// <summary>
    /// Resolves the route of a chain ordered by priority.
    /// </summary>
    /// <param name="chain">The chain links.</param>
    /// <returns>The route.</returns>
    /// <exception cref="ArgumentException">The chain is empty.</exception>
    public static ChainRoute Resolve(IReadOnlyList<ChainLinkConfig> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        if (chain.Count == 0)
        {
            throw new ArgumentException("The chain has no links; add a link to the counterpart endpoint first.", nameof(chain));
        }

        var ordered = chain.OrderBy(link => link.Priority).ThenBy(link => link.Name, StringComparer.Ordinal).ToArray();
        var endpoint = ordered[^1];
        return new ChainRoute(endpoint, endpoint.Peer.Ssh, ordered[..^1].Select(link => link.Peer.Ssh).ToArray());
    }
}
