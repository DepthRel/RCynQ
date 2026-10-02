namespace RCynQ.Core.Api;

/// <summary>
/// Chooses the route of a link.
/// </summary>
/// <param name="RouteId">The route to use whether it answers or not, or <see langword="null"/> for the automatic choice.</param>
public sealed record SelectRouteRequest(Guid? RouteId);
