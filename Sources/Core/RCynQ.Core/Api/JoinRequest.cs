namespace RCynQ.Core.Api;

/// <summary>
/// Asks the node to join the node that wrote an invitation.
/// </summary>
/// <param name="Invitation">The invitation as one line of text.</param>
public sealed record JoinRequest(string Invitation);
