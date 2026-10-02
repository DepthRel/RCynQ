using System.Globalization;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Localization;

namespace RCynQ.UI.ViewModels.Items;

/// <summary>
/// One route to a node, shown under the node on the role and nodes tab.
/// </summary>
/// <remarks>
/// The row is rebuilt whenever the service reports the routes again, so it holds no state of its own.
/// </remarks>
public sealed class LinkRouteItemViewModel
{
    /// <summary>The localizer of the texts.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The route as the service reported it.</summary>
    private readonly LinkRouteDto _route;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRouteItemViewModel"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the texts.</param>
    /// <param name="linkId">The link the route belongs to.</param>
    /// <param name="route">The route.</param>
    /// <param name="isPinned">Whether the route was chosen by hand.</param>
    public LinkRouteItemViewModel(ILocalizer localizer, Guid linkId, LinkRouteDto route, bool isPinned)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _route = route ?? throw new ArgumentNullException(nameof(route));
        LinkId = linkId;
        IsPinned = isPinned;
    }

    /// <summary>Gets the link the route belongs to.</summary>
    public Guid LinkId { get; }

    /// <summary>Gets the route identifier.</summary>
    public Guid RouteId => _route.Id;

    /// <summary>Gets the display name of the route.</summary>
    public string Name => _route.Name;

    /// <summary>Gets the address of the node on this route.</summary>
    public string Address => string.Create(CultureInfo.InvariantCulture, $"{_route.Host}:{_route.SshPort}");

    /// <summary>Gets the preference of the route; lower values are preferred.</summary>
    public int Priority => _route.Priority;

    /// <summary>Gets the preference as text.</summary>
    public string PriorityText => _localizer.Format("Routes.Priority", _route.Priority);

    /// <summary>Gets a value indicating whether the route was chosen by hand.</summary>
    public bool IsPinned { get; }

    /// <summary>Gets a value indicating whether the traffic to the node goes this way now.</summary>
    public bool IsActive => _route.IsActive;

    /// <summary>Gets a value indicating whether the node answered on the route at the last check.</summary>
    public bool IsReachable => _route.Reachability == RouteReachability.Reachable;

    /// <summary>Gets a value indicating whether the node did not answer on the route at the last check.</summary>
    public bool IsUnreachable => _route.Reachability == RouteReachability.Unreachable;

    /// <summary>Gets the outcome of the last check as text, with the latency when the node answered.</summary>
    public string StatusText => _route.Reachability switch
    {
        RouteReachability.Reachable when _route.LatencyMilliseconds is { } latency
            => _localizer.Format("Routes.ReachableIn", Math.Round(latency).ToString("0", _localizer.Culture)),
        RouteReachability.Reachable => _localizer.Get("Routes.Reachable"),
        RouteReachability.Unreachable => _localizer.Get("Routes.Unreachable"),
        _ => _localizer.Get("Routes.Unknown")
    };

    /// <summary>Gets the mark of the route in use, or an empty text.</summary>
    public string ActiveText => IsActive ? _localizer.Get("Routes.Active") : string.Empty;
}
