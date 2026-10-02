namespace RCynQ.Core.Config;

/// <summary>
/// Bindable settings of one more address the peer of a chain link is reachable at.
/// </summary>
/// <remarks>
/// The address written in the link itself (<see cref="ChainLinkOptions.Host"/>) is always a route; the entries of
/// <see cref="ChainLinkOptions.Routes"/> are the other ways to the same node.
/// </remarks>
public sealed class LinkRouteOptions
{
    /// <summary>
    /// Gets or sets the display name of the route. Defaults to <see cref="Host"/>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the host name or address of the peer on this route.
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SSH port of the peer on this route, or <see langword="null"/> to use the one of the link.
    /// </summary>
    public int? SshPort { get; set; }

    /// <summary>
    /// Gets or sets the SSH user on this route, or <see langword="null"/> to use the one of the link.
    /// </summary>
    public string? SshUser { get; set; }

    /// <summary>
    /// Gets or sets the preference of the route; lower values are preferred, equal values are decided by the latency.
    /// </summary>
    public int Priority { get; set; }
}
