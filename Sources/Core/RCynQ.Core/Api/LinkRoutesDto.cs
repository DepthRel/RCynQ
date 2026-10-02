namespace RCynQ.Core.Api;

/// <summary>
/// The routes of one link.
/// </summary>
/// <param name="LinkId">The link identifier.</param>
/// <param name="PinnedRouteId">The route chosen by hand, or <see langword="null"/> when the node chooses.</param>
/// <param name="Routes">The routes, ordered by preference.</param>
public sealed record LinkRoutesDto(Guid LinkId, Guid? PinnedRouteId, IReadOnlyList<LinkRouteDto> Routes);
