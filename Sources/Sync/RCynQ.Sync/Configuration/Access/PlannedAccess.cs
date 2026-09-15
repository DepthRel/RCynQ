using RCynQ.Core.Config;

namespace RCynQ.Sync.Configuration.Access;

/// <summary>
/// A restricted <c>authorized_keys</c> line for a neighbour.
/// </summary>
/// <param name="Peer">The neighbour.</param>
/// <param name="Line">The line.</param>
public sealed record PlannedAccess(PeerDescriptor Peer, string Line);
