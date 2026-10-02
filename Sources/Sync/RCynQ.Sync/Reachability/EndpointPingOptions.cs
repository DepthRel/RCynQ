namespace RCynQ.Sync.Reachability;

/// <summary>
/// How the link to the counterpart endpoint is checked.
/// </summary>
public sealed class EndpointPingOptions
{
    /// <summary>
    /// Gets or sets how long the outcome of a ping is shown before a request for the state starts a new one. A ping
    /// opens an SSH connection through the mediator, so it is not repeated on every look at the state.
    /// </summary>
    public TimeSpan CacheTime { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets how long a ping may take before the peer counts as not answering.
    /// </summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Gets or sets a value indicating whether a request for the state waits for the ping. The service answers at once
    /// and reports the outcome with an event, so the application stays responsive; a command of the command line has
    /// nobody to report to later and waits.
    /// </summary>
    public bool WaitForResult { get; set; }
}
