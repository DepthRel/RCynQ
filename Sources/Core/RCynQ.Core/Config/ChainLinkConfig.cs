using RCynQ.Core.Enums;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Config;

/// <summary>
/// Describes one link of the chain from the local node towards the counterpart endpoint.
/// </summary>
/// <remarks>
/// <para>
/// On Workstation and Origin the chain is ordered by <see cref="Priority"/>: every link except the last
/// one points at a Relay used as SSH jump host, and the last link points at the counterpart
/// endpoint whose loopback patch endpoint listens on <see cref="RemotePort"/>.
/// </para>
/// <para>
/// On a Relay the chain lists the neighbours whose transports the relay must keep up.
/// </para>
/// <para>
/// The peer may be reachable at more than one address; these are the <see cref="Routes"/> of the link. The address of
/// <see cref="Peer"/> is always the one in use, so code that follows the chain needs to know nothing about routes. A link
/// without routes has the single address of its peer.
/// </para>
/// </remarks>
public sealed record ChainLinkConfig
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ChainLinkConfig"/> record.
    /// </summary>
    /// <param name="id">The unique link identifier.</param>
    /// <param name="name">The display name of the link.</param>
    /// <param name="transport">The primary transport of the link.</param>
    /// <param name="peer">The peer at the far end of the link.</param>
    /// <param name="localPort">
    /// The local TCP port used for the SSH local forward to the endpoint, or <c>0</c> for an ephemeral port.
    /// </param>
    /// <param name="remotePort">
    /// For the endpoint link, the loopback port of the peer's patch endpoint; for relay links, the
    /// port the relay may open towards its next hop (usually <c>22</c>).
    /// </param>
    /// <param name="priority">The position of the link in the chain; lower values come first.</param>
    /// <param name="fallbackTransports">Transports tried in order when the primary transport fails.</param>
    /// <param name="transportSettings">Transport specific settings, for example <c>configPath</c> for AmneziaWG.</param>
    /// <param name="routes">The addresses the peer is reachable at, or <see langword="null"/> when it has only its own.</param>
    /// <param name="pinnedRouteId">
    /// The route chosen by hand, which is used whether it answers or not; <see langword="null"/> lets the node choose.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="peer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// The name is blank, a transport is not defined, two routes share an identifier or an address, or the pinned route
    /// is not one of the routes.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">A port or the priority is out of range.</exception>
    public ChainLinkConfig(
        Guid id,
        string name,
        TransportKind transport,
        PeerDescriptor peer,
        int localPort,
        int remotePort,
        int priority = 0,
        IReadOnlyList<TransportKind>? fallbackTransports = null,
        IReadOnlyDictionary<string, string>? transportSettings = null,
        IReadOnlyList<LinkRoute>? routes = null,
        Guid? pinnedRouteId = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Link identifier must not be empty.", nameof(id));
        }

        Id = id;
        Name = Guard.NotBlank(name, nameof(name));
        Transport = Guard.Defined(transport, nameof(transport));
        Peer = Guard.NotNull(peer, nameof(peer));
        LocalPort = Guard.Port(localPort, nameof(localPort), allowZero: true);
        RemotePort = Guard.Port(remotePort, nameof(remotePort));
        Priority = Guard.NonNegative(priority, nameof(priority));
        FallbackTransports = (fallbackTransports ?? Array.Empty<TransportKind>())
            .Select(kind => Guard.Defined(kind, nameof(fallbackTransports)))
            .Where(kind => kind != transport)
            .Distinct()
            .ToArray();
        TransportSettings = transportSettings is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(transportSettings, StringComparer.OrdinalIgnoreCase);
        Routes = ValidateRoutes(routes);
        if (pinnedRouteId is { } pinned && Routes.All(route => route.Id != pinned))
        {
            throw new ArgumentException($"Pinned route {pinned} is not a route of link '{Name}'.", nameof(pinnedRouteId));
        }

        PinnedRouteId = pinnedRouteId;
    }

    /// <summary>
    /// Gets the unique link identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the display name of the link.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the primary transport of the link.
    /// </summary>
    public TransportKind Transport { get; }

    /// <summary>
    /// Gets the peer at the far end of the link.
    /// </summary>
    public PeerDescriptor Peer { get; }

    /// <summary>
    /// Gets the local TCP port for the SSH local forward, or <c>0</c> for an ephemeral port.
    /// </summary>
    public int LocalPort { get; }

    /// <summary>
    /// Gets the remote TCP port reached through the link.
    /// </summary>
    public int RemotePort { get; }

    /// <summary>
    /// Gets the position of the link in the chain; lower values come first.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Gets the transports tried in order when the primary transport fails. Never contains the primary transport.
    /// </summary>
    public IReadOnlyList<TransportKind> FallbackTransports { get; }

    /// <summary>
    /// Gets the transport specific settings (case-insensitive keys).
    /// </summary>
    public IReadOnlyDictionary<string, string> TransportSettings { get; }

    /// <summary>
    /// Gets the addresses the peer is reachable at, ordered by preference. Empty when the peer has only its own address.
    /// </summary>
    public IReadOnlyList<LinkRoute> Routes { get; }

    /// <summary>
    /// Gets the identifier of the route chosen by hand, or <see langword="null"/> when the node chooses the route itself.
    /// </summary>
    public Guid? PinnedRouteId { get; }

    /// <summary>
    /// Gets the route in use: the one that leads to the address of <see cref="Peer"/>, or <see langword="null"/> when
    /// the link has no routes.
    /// </summary>
    public LinkRoute? ActiveRoute => Routes.FirstOrDefault(route => route.LeadsTo(Peer.Ssh));

    /// <summary>
    /// Gets a value indicating whether there is more than one route to choose from.
    /// </summary>
    public bool HasAlternativeRoutes => Routes.Count > 1;

    /// <summary>
    /// Creates a copy of this link that uses a different primary transport.
    /// </summary>
    /// <param name="transport">The transport to use.</param>
    /// <returns>A new link configuration with the same identity and settings.</returns>
    public ChainLinkConfig WithTransport(TransportKind transport)
        => new(Id, Name, transport, Peer, LocalPort, RemotePort, Priority, FallbackTransports, TransportSettings, Routes, PinnedRouteId);

    /// <summary>
    /// Creates a copy of this link that leads to another description of the peer.
    /// </summary>
    /// <param name="peer">The peer description to use.</param>
    /// <param name="remotePort">The remote port to use, or <see langword="null"/> to keep the current one.</param>
    /// <returns>A new link configuration with the same identity and settings.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="peer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="remotePort"/> is outside 1..65535.</exception>
    public ChainLinkConfig WithPeer(PeerDescriptor peer, int? remotePort = null)
        => new(Id, Name, Transport, peer, LocalPort, remotePort ?? RemotePort, Priority, FallbackTransports, TransportSettings, Routes, PinnedRouteId);

    /// <summary>
    /// Creates a copy of this link with other routes to its peer.
    /// </summary>
    /// <param name="routes">The routes; an empty list leaves the peer with its own address only.</param>
    /// <param name="pinnedRouteId">The route chosen by hand, or <see langword="null"/> to let the node choose.</param>
    /// <returns>A new link configuration with the same identity and settings.</returns>
    /// <exception cref="ArgumentException">The routes are not valid or the pinned route is not one of them.</exception>
    public ChainLinkConfig WithRoutes(IReadOnlyList<LinkRoute>? routes, Guid? pinnedRouteId = null)
        => new(Id, Name, Transport, Peer, LocalPort, RemotePort, Priority, FallbackTransports, TransportSettings, routes, pinnedRouteId);

    /// <summary>
    /// Creates a copy of this link whose peer is addressed through the given route.
    /// </summary>
    /// <param name="routeId">The identifier of one of the <see cref="Routes"/>.</param>
    /// <returns>A new link configuration; this instance when the route is the one in use.</returns>
    /// <exception cref="KeyNotFoundException">The link has no such route.</exception>
    public ChainLinkConfig WithActiveRoute(Guid routeId)
    {
        var route = Routes.FirstOrDefault(candidate => candidate.Id == routeId)
            ?? throw new KeyNotFoundException($"Link '{Name}' has no route {routeId}.");
        return route.LeadsTo(Peer.Ssh) ? this : WithPeer(Peer.WithAddress(route.Ssh));
    }

    /// <summary>
    /// Creates a copy of this link with a route pinned by hand, or with the choice handed back to the node.
    /// </summary>
    /// <param name="routeId">The route to use whether it answers or not, or <see langword="null"/> for the automatic choice.</param>
    /// <returns>A new link configuration; a pinned route becomes the one in use.</returns>
    /// <exception cref="KeyNotFoundException">The link has no such route.</exception>
    public ChainLinkConfig WithPinnedRoute(Guid? routeId)
        => routeId is { } pinned
            ? WithActiveRoute(pinned).WithRoutes(Routes, pinned)
            : WithRoutes(Routes, null);

    /// <summary>
    /// Orders the routes and rejects duplicates.
    /// </summary>
    /// <param name="routes">The routes, or <see langword="null"/>.</param>
    /// <returns>The routes ordered by preference and name.</returns>
    /// <exception cref="ArgumentException">Two routes share an identifier or an address.</exception>
    private static LinkRoute[] ValidateRoutes(IReadOnlyList<LinkRoute>? routes)
    {
        if (routes is null || routes.Count == 0)
        {
            return Array.Empty<LinkRoute>();
        }

        var ordered = routes
            .Select(route => Guard.NotNull(route, nameof(routes)))
            .OrderBy(route => route.Priority)
            .ThenBy(route => route.Name, StringComparer.Ordinal)
            .ToArray();
        for (var i = 0; i < ordered.Length; i++)
        {
            for (var j = i + 1; j < ordered.Length; j++)
            {
                if (ordered[i].Id == ordered[j].Id)
                {
                    throw new ArgumentException($"Route {ordered[i].Id} is listed twice.", nameof(routes));
                }

                if (ordered[i].LeadsTo(ordered[j].Ssh))
                {
                    throw new ArgumentException($"Routes '{ordered[i].Name}' and '{ordered[j].Name}' lead to the same address {ordered[i].Ssh}.", nameof(routes));
                }
            }
        }

        return ordered;
    }
}
