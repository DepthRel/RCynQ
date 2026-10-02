namespace RCynQ.Core.Api;

/// <summary>
/// Changes the preference of a route.
/// </summary>
/// <param name="Priority">The preference; lower values are preferred, equal values are decided by the latency.</param>
public sealed record SetRoutePriorityRequest(int Priority);
