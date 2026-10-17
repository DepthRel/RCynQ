namespace RCynQ.Core.Api;

/// <summary>
/// What an invited node sends to enroll: the ticket it holds and its own card.
/// </summary>
/// <param name="TicketId">The ticket identifier.</param>
/// <param name="Secret">The secret of the ticket.</param>
/// <param name="Card">The card of the enrolling node.</param>
/// <param name="Language">
/// The language code the answer is worded in, or <see langword="null"/>; it travels in the request because an
/// enrollment over SSH carries no HTTP headers of the enrolling node.
/// </param>
public sealed record EnrollmentRequest(Guid TicketId, string Secret, ExportedNodeConfiguration Card, string? Language = null);
