using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// A link an import can replace the peer of.
/// </summary>
/// <param name="LinkId">The link identifier.</param>
/// <param name="LinkName">The link name.</param>
/// <param name="PeerId">The peer the link leads to now.</param>
/// <param name="PeerName">The display name of that peer.</param>
/// <param name="PeerRole">The role of that peer.</param>
/// <param name="Host">The address of that peer in use.</param>
public sealed record ImportCandidate(Guid LinkId, string LinkName, NodeId PeerId, string PeerName, NodeRole PeerRole, string Host);
