using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// The answer of a node to the counterpart endpoint that asked whether it is ready to exchange patches.
/// </summary>
/// <param name="NodeId">The identifier of the node that answers, so the asking node can tell it reached the node it meant.</param>
/// <param name="Role">The role of the node that answers.</param>
/// <param name="AnsweredAtUtc">The time of the answer on the node that answers.</param>
public sealed record PeerPingResponse(string NodeId, NodeRole Role, DateTimeOffset AnsweredAtUtc);
