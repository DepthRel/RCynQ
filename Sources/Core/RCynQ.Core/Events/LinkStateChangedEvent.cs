using RCynQ.Core.State;

namespace RCynQ.Core.Events;

/// <summary>
/// Event published when a transport link changes state.
/// </summary>
/// <param name="State">The new link state.</param>
public sealed record LinkStateChangedEvent(LinkState State);
