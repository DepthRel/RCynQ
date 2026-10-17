namespace RCynQ.Core.Api;

/// <summary>
/// The answer of the inviting node to an enrollment.
/// </summary>
/// <param name="Accepted">Whether the node was taken.</param>
/// <param name="Message">The message for the user of the enrolling node, in the language of its request.</param>
/// <param name="RelayTicket">
/// A ticket of this Relay for the opposite endpoint, which the enrolled endpoint passes on in its own invitation;
/// <see langword="null"/> when the inviting node is not a Relay.
/// </param>
/// <param name="AccessPending">
/// Whether the inviting node could not allow the key of the enrolled node yet (its <c>authorized_keys</c> needs
/// administrator rights); someone has to confirm it there.
/// </param>
public sealed record EnrollmentResponse(bool Accepted, string Message, InvitationTicket? RelayTicket = null, bool AccessPending = false);
