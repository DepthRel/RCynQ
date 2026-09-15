using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// Result of importing a peer configuration.
/// </summary>
/// <param name="PeerId">The imported peer.</param>
/// <param name="PeerRole">The role of the peer.</param>
/// <param name="LinkCreated">Whether a new chain link was created.</param>
/// <param name="LinkId">The created or updated link, or <see langword="null"/>.</param>
/// <param name="Message">A human-readable summary.</param>
public sealed record ImportResult(NodeId PeerId, NodeRole PeerRole, bool LinkCreated, Guid? LinkId, string Message);
