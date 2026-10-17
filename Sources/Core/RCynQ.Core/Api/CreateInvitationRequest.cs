using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Asks the node for an invitation.
/// </summary>
/// <param name="Role">The role of the node that is invited.</param>
/// <param name="LifetimeHours">How long the invitation stays valid, in hours (1 to 168).</param>
public sealed record CreateInvitationRequest(NodeRole Role, int LifetimeHours);
