namespace RCynQ.Sync.Routing;

/// <summary>
/// How the routes of the links are checked.
/// </summary>
public sealed class RouteCheckOptions
{
    /// <summary>
    /// Gets or sets the time between two checks of all routes.
    /// </summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Gets or sets how long one route may take to answer before it counts as not answering.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Gets or sets how much faster another route of the same preference must be before the node leaves the route in
    /// use for it, as a share of the latency of the route in use. Keeps two routes of similar speed from taking turns.
    /// </summary>
    public double SwitchAdvantage { get; set; } = 0.25;

    /// <summary>
    /// Gets or sets the smallest difference in latency that counts as faster at all.
    /// </summary>
    public TimeSpan SwitchMinimum { get; set; } = TimeSpan.FromMilliseconds(10);
}
