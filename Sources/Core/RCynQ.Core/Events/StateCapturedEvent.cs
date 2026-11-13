namespace RCynQ.Core.Events;

/// <summary>
/// Published when the node has looked at its working tree and its sync base: the summary of its state now tells the
/// pending changes and whether the sync base is ready.
/// </summary>
/// <remarks>
/// The first look of the service runs in the background after the start and may take minutes on a large tree; a client
/// that showed the preliminary summary learns from this event that the full one is there.
/// </remarks>
/// <param name="TimestampUtc">When the look was done.</param>
public sealed record StateCapturedEvent(DateTimeOffset TimestampUtc);
