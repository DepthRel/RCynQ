namespace RCynQ.Core.Api;

/// <summary>
/// An invitation the node created.
/// </summary>
/// <param name="Text">The invitation as one line of text, to pass to the invited node.</param>
/// <param name="Summary">The open invitation as the list of invitations shows it.</param>
/// <param name="AccessPending">
/// Whether the one-time key of the invitation could not be written to <c>authorized_keys</c> yet because that needs
/// administrator rights; until it is, the invited node cannot log in.
/// </param>
/// <param name="Notes">Localized notes for the user, such as a Relay the invited node must know already; may be empty.</param>
public sealed record CreatedInvitation(string Text, InvitationDto Summary, bool AccessPending, IReadOnlyList<string> Notes);
