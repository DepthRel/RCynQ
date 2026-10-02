using System.Collections.Concurrent;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;

namespace RCynQ.Sync.Routing;

/// <summary>
/// Default <see cref="ILinkRouteSelector"/>.
/// </summary>
/// <remarks>
/// <para>
/// The route in use is not kept apart from the link: choosing a route writes its address into the peer of the link.
/// Everything that follows the chain (patches, the Git channel, the ping, the diagnostics) therefore takes the chosen
/// route without knowing that routes exist.
/// </para>
/// <para>
/// The choice: a route pinned by hand is used whether it answers or not. Otherwise the routes that answered the last
/// check compete; the lowest <see cref="LinkRoute.Priority"/> wins and the latency decides among equals. While no route
/// answers the link stays where it is. The node returns to a better route as soon as a check finds it answering.
/// </para>
/// </remarks>
public sealed class LinkRouteSelector : ILinkRouteSelector, IDisposable
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>Checks one route.</summary>
    private readonly ILinkRouteProbe _probe;

    /// <summary>The event publisher.</summary>
    private readonly IEventPublisher _events;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>How the routes are checked.</summary>
    private readonly RouteCheckOptions _options;

    /// <summary>The outcome of the last check of every route, by route identifier.</summary>
    private readonly ConcurrentDictionary<Guid, RouteCheck> _checks = new();

    /// <summary>One change of the routes at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Released when a check is asked for.</summary>
    private readonly SemaphoreSlim _requested = new(0, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRouteSelector"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="probe">Checks one route.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="options">How the routes are checked.</param>
    public LinkRouteSelector(NodeConfig node, IChainLinkRepository links, ILinkRouteProbe probe, IEventPublisher events, TimeProvider timeProvider, RouteCheckOptions options)
    {
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkRoutesDto>> GetRoutesAsync(CancellationToken cancellationToken = default)
        => (await _links.GetAllAsync(cancellationToken))
            .Where(link => link.Routes.Count > 0)
            .Select(ToDto)
            .ToArray();

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkRoutesDto>> CheckAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            // The order the route of a patch is resolved in: every link is reached through the ones before it.
            var chain = (await _links.GetAllAsync(cancellationToken))
                .OrderBy(link => link.Priority)
                .ThenBy(link => link.Name, StringComparer.Ordinal)
                .ToList();
            var checkedLinks = new List<LinkRoutesDto>();
            for (var i = 0; i < chain.Count; i++)
            {
                if (!chain[i].HasAlternativeRoutes)
                {
                    continue;
                }

                // A Relay connects to each of its neighbours itself.
                var hops = _node.Role == NodeRole.Relay ? Array.Empty<ChainLinkConfig>() : chain.Take(i).ToArray();
                var (link, switched) = await CheckLinkAsync(chain[i], hops, cancellationToken);
                chain[i] = link;
                var routes = ToDto(link);
                checkedLinks.Add(routes);
                await _events.PublishLinkRoutesAsync(new LinkRoutesChangedEvent(routes, switched), cancellationToken);
            }

            return checkedLinks;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public Task<LinkRoutesDto> SelectAsync(Guid linkId, Guid? routeId, CancellationToken cancellationToken = default)
        => ChangeAsync(linkId, link => link.WithPinnedRoute(routeId), cancellationToken);

    /// <inheritdoc />
    public Task<LinkRoutesDto> SetPriorityAsync(Guid linkId, Guid routeId, int priority, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(priority);
        return ChangeAsync(
            linkId,
            link =>
            {
                _ = RouteOf(link, routeId);
                return link.WithRoutes(
                    link.Routes.Select(route => route.Id == routeId ? route.WithPriority(priority) : route).ToArray(),
                    link.PinnedRouteId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<LinkRoutesDto> RemoveAsync(Guid linkId, Guid routeId, CancellationToken cancellationToken = default)
        => ChangeAsync(
            linkId,
            link =>
            {
                var removed = RouteOf(link, routeId);
                var left = link.Routes.Where(route => route.Id != routeId).ToArray();
                _checks.TryRemove(routeId, out _);
                if (left.Length == 0)
                {
                    return link.WithRoutes(null);
                }

                // The peer keeps an address that is still a route of the link.
                var moved = removed.LeadsTo(link.Peer.Ssh) ? link.WithPeer(link.Peer.WithAddress(left[0].Ssh)) : link;

                // One address left is no choice: the link is back to what it was before it had routes.
                return left.Length == 1
                    ? moved.WithPeer(moved.Peer.WithAddress(left[0].Ssh)).WithRoutes(null)
                    : moved.WithRoutes(left, link.PinnedRouteId == routeId ? null : link.PinnedRouteId);
            },
            cancellationToken);

    /// <inheritdoc />
    public void RequestCheck()
    {
        try
        {
            _requested.Release();
        }
        catch (SemaphoreFullException)
        {
            // A check is asked for already.
        }
        catch (ObjectDisposedException)
        {
            // The service is stopping.
        }
    }

    /// <inheritdoc />
    public async Task WaitForCheckAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
        => await _requested.WaitAsync(timeout, cancellationToken);

    /// <summary>
    /// Releases the locks.
    /// </summary>
    public void Dispose()
    {
        _gate.Dispose();
        _requested.Dispose();
    }

    /// <summary>
    /// Chooses the route a link should use, given what the last check found.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="checks">The outcome of the check of each route, by route identifier; a route without one counts as not answering.</param>
    /// <param name="options">How the routes are checked.</param>
    /// <returns>The route to use, or <see langword="null"/> when the link has no route to move to.</returns>
    public static LinkRoute? Choose(ChainLinkConfig link, IReadOnlyDictionary<Guid, RouteProbeResult> checks, RouteCheckOptions options)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(checks);
        ArgumentNullException.ThrowIfNull(options);

        if (link.PinnedRouteId is { } pinned)
        {
            return link.Routes.First(route => route.Id == pinned);
        }

        var current = link.ActiveRoute;
        var answering = link.Routes.Where(route => checks.TryGetValue(route.Id, out var check) && check.Reachable).ToArray();
        if (answering.Length == 0)
        {
            // Nothing answers: moving would only trade one silent route for another.
            return current;
        }

        var preferred = answering.Min(route => route.Priority);
        var candidates = answering.Where(route => route.Priority == preferred).ToArray();
        var fastest = candidates.MinBy(route => LatencyOf(route, checks))!;
        if (current is null || candidates.All(route => route.Id != current.Id) || fastest.Id == current.Id)
        {
            return fastest;
        }

        // Two routes of similar speed must not take turns: the route in use is left only for a clearly faster one.
        var gain = LatencyOf(current, checks) - LatencyOf(fastest, checks);
        var needed = TimeSpan.FromTicks(Math.Max(
            options.SwitchMinimum.Ticks,
            (long)(LatencyOf(current, checks).Ticks * options.SwitchAdvantage)));
        return gain > needed ? fastest : current;
    }

    /// <summary>
    /// Checks the routes of one link and moves it to the best one.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="hops">The links passed on the way to the peer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The link as it is stored after the check, and whether it moved to another route.</returns>
    private async Task<(ChainLinkConfig Link, bool Switched)> CheckLinkAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> hops, CancellationToken cancellationToken)
    {
        var outcomes = await Task.WhenAll(link.Routes.Select(route => ProbeAsync(link, route, hops, cancellationToken)));
        var now = _timeProvider.GetUtcNow();
        var checks = new Dictionary<Guid, RouteProbeResult>();
        for (var i = 0; i < link.Routes.Count; i++)
        {
            checks[link.Routes[i].Id] = outcomes[i];
            _checks[link.Routes[i].Id] = new RouteCheck(outcomes[i], now);
        }

        // The check took a while: the link is read again so a change made meanwhile (an import, a pin) is not undone.
        var stored = await _links.GetAsync(link.Id, cancellationToken);
        if (stored is null || !stored.HasAlternativeRoutes)
        {
            return (stored ?? link, false);
        }

        var chosen = Choose(stored, checks, _options);
        if (chosen is null || chosen.Id == stored.ActiveRoute?.Id)
        {
            return (stored, false);
        }

        var moved = stored.WithActiveRoute(chosen.Id);
        await _links.UpsertAsync(moved, cancellationToken);
        return (moved, true);
    }

    /// <summary>
    /// Checks one route; a check that fails for any reason counts as a route that does not answer.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="route">The route.</param>
    /// <param name="hops">The links passed on the way to the peer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    private async Task<RouteProbeResult> ProbeAsync(ChainLinkConfig link, LinkRoute route, IReadOnlyList<ChainLinkConfig> hops, CancellationToken cancellationToken)
    {
        try
        {
            return await _probe.ProbeAsync(link, route, hops, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return RouteProbeResult.Unreachable;
        }
    }

    /// <summary>
    /// Applies a change to a stored link and publishes its routes.
    /// </summary>
    /// <param name="linkId">The link.</param>
    /// <param name="change">The change.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes of the changed link.</returns>
    /// <exception cref="KeyNotFoundException">There is no such link or route.</exception>
    private async Task<LinkRoutesDto> ChangeAsync(Guid linkId, Func<ChainLinkConfig, ChainLinkConfig> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var link = await _links.GetAsync(linkId, cancellationToken)
                ?? throw new KeyNotFoundException($"Link {linkId} is not configured.");
            var changed = change(link);
            await _links.UpsertAsync(changed, cancellationToken);
            var routes = ToDto(changed);
            await _events.PublishLinkRoutesAsync(
                new LinkRoutesChangedEvent(routes, changed.ActiveRoute?.Id != link.ActiveRoute?.Id),
                cancellationToken);
            return routes;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Finds a route of a link.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="routeId">The route identifier.</param>
    /// <returns>The route.</returns>
    /// <exception cref="KeyNotFoundException">The link has no such route.</exception>
    private static LinkRoute RouteOf(ChainLinkConfig link, Guid routeId)
        => link.Routes.FirstOrDefault(route => route.Id == routeId)
            ?? throw new KeyNotFoundException($"Link '{link.Name}' has no route {routeId}.");

    /// <summary>
    /// Describes the routes of a link with the outcome of their last check.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <returns>The routes.</returns>
    private LinkRoutesDto ToDto(ChainLinkConfig link)
    {
        var active = link.ActiveRoute;
        return new LinkRoutesDto(
            link.Id,
            link.PinnedRouteId,
            link.Routes.Select(route =>
            {
                var known = _checks.TryGetValue(route.Id, out var check) ? check : null;
                return new LinkRouteDto(
                    route.Id,
                    route.Name,
                    route.Ssh.Host,
                    route.Ssh.Port,
                    route.Ssh.User,
                    route.Priority,
                    route.Id == active?.Id,
                    known is null ? RouteReachability.Unknown : known.Result.Reachable ? RouteReachability.Reachable : RouteReachability.Unreachable,
                    known?.Result.Latency?.TotalMilliseconds,
                    known?.CheckedAtUtc);
            }).ToArray());
    }

    /// <summary>
    /// Gets the latency a route answered with; a route without one is slower than any other.
    /// </summary>
    /// <param name="route">The route.</param>
    /// <param name="checks">The outcome of the check of each route.</param>
    /// <returns>The latency.</returns>
    private static TimeSpan LatencyOf(LinkRoute route, IReadOnlyDictionary<Guid, RouteProbeResult> checks)
        => checks.TryGetValue(route.Id, out var check) && check.Latency is { } latency ? latency : TimeSpan.MaxValue;

    /// <summary>
    /// The outcome of the last check of a route.
    /// </summary>
    /// <param name="Result">What the check found.</param>
    /// <param name="CheckedAtUtc">When the route was checked.</param>
    private sealed record RouteCheck(RouteProbeResult Result, DateTimeOffset CheckedAtUtc);
}
