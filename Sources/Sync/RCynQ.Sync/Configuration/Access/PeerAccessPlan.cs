using RCynQ.Core.Config;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// The <c>authorized_keys</c> lines a node needs for its neighbours.
/// </summary>
/// <param name="Entries">The lines to write.</param>
/// <param name="MissingKeys">Neighbours that need access but have no SSH public key.</param>
/// <param name="WaitingForCounterpart">Neighbours of a Relay whose opposite endpoint is not imported yet.</param>
public sealed record PeerAccessPlan(
    IReadOnlyList<PlannedAccess> Entries,
    IReadOnlyList<PeerDescriptor> MissingKeys,
    IReadOnlyList<PeerDescriptor> WaitingForCounterpart);
