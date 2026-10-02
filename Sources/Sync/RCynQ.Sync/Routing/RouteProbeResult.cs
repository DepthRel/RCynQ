namespace RCynQ.Sync.Routing;

/// <summary>
/// The outcome of checking one route.
/// </summary>
/// <param name="Reachable">Whether the peer answered on the route.</param>
/// <param name="Latency">How long the answer took, or <see langword="null"/> when there was none.</param>
public sealed record RouteProbeResult(bool Reachable, TimeSpan? Latency)
{
    /// <summary>
    /// Gets the outcome of a route nobody answered on.
    /// </summary>
    public static RouteProbeResult Unreachable { get; } = new(false, null);
}
