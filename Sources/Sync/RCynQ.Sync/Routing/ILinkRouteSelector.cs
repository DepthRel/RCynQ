using RCynQ.Core.Api;

namespace RCynQ.Sync.Routing;

/// <summary>
/// Knows the routes of the links, which of them answer, and chooses the one the traffic takes.
/// </summary>
/// <remarks>
/// A link with fewer than two routes is left alone: nothing is checked and nothing is changed for it, so a node whose
/// links have one address each behaves as it did before routes existed.
/// </remarks>
public interface ILinkRouteSelector
{
    /// <summary>
    /// Gets the routes of every link that has any, with the outcome of their last check.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes per link.</returns>
    Task<IReadOnlyList<LinkRoutesDto>> GetRoutesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks every route of every link that has a choice and moves each such link to its best route.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes of the checked links.</returns>
    Task<IReadOnlyList<LinkRoutesDto>> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins a route by hand, or hands the choice back to the node.
    /// </summary>
    /// <param name="linkId">The link.</param>
    /// <param name="routeId">The route to use whether it answers or not, or <see langword="null"/> for the automatic choice.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes of the link.</returns>
    /// <exception cref="KeyNotFoundException">There is no such link or route.</exception>
    Task<LinkRoutesDto> SelectAsync(Guid linkId, Guid? routeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Changes the preference of a route.
    /// </summary>
    /// <param name="linkId">The link.</param>
    /// <param name="routeId">The route.</param>
    /// <param name="priority">The preference; lower values are preferred.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes of the link.</returns>
    /// <exception cref="KeyNotFoundException">There is no such link or route.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="priority"/> is negative.</exception>
    Task<LinkRoutesDto> SetPriorityAsync(Guid linkId, Guid routeId, int priority, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a route. The link moves to another route when the removed one was in use; a link left with one route has
    /// that address and no routes.
    /// </summary>
    /// <param name="linkId">The link.</param>
    /// <param name="routeId">The route.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The routes of the link.</returns>
    /// <exception cref="KeyNotFoundException">There is no such link or route.</exception>
    Task<LinkRoutesDto> RemoveAsync(Guid linkId, Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks for a check of the routes soon, because the route in use seems not to answer.
    /// </summary>
    void RequestCheck();

    /// <summary>
    /// Waits until a check was asked for with <see cref="RequestCheck"/> or the time is up.
    /// </summary>
    /// <param name="timeout">The longest time to wait.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task that completes when the next check is due.</returns>
    Task WaitForCheckAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}
