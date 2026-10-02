using RCynQ.Core.Api;

namespace RCynQ.Core.Events;

/// <summary>
/// Event published when the routes of a link were checked or the route in use changed.
/// </summary>
/// <param name="Routes">The routes of the link with the outcome of the check.</param>
/// <param name="Switched">Whether the link moved to another route.</param>
public sealed record LinkRoutesChangedEvent(LinkRoutesDto Routes, bool Switched = false);
