using RCynQ.Core.Config;

namespace RCynQ.Sync.Routing;

/// <summary>
/// Checks whether the peer of a link answers at the address of one of its routes.
/// </summary>
public interface ILinkRouteProbe
{
    /// <summary>
    /// Checks one route.
    /// </summary>
    /// <param name="link">The link the route belongs to.</param>
    /// <param name="route">The route to check.</param>
    /// <param name="hops">
    /// The links passed on the way to the peer, in order, with the addresses in use; empty when this node connects to
    /// the peer itself.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; a route that does not answer is an outcome, not an exception.</returns>
    Task<RouteProbeResult> ProbeAsync(ChainLinkConfig link, LinkRoute route, IReadOnlyList<ChainLinkConfig> hops, CancellationToken cancellationToken = default);
}
