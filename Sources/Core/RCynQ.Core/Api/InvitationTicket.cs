using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// The single-use permission to enroll at one node, carried by an invitation. It is a secret: whoever holds it may join
/// that node once until it expires.
/// </summary>
/// <param name="Id">The ticket identifier, as the inviting node stores it.</param>
/// <param name="Secret">The random secret of the ticket (Base64url); the inviting node keeps only its hash.</param>
/// <param name="Role">The role the enrolling node must have.</param>
/// <param name="ExpiresAtUtc">When the ticket stops being accepted.</param>
/// <param name="EnrollmentKey">
/// The private part of the one-time SSH key (OpenSSH format) that logs in to the SSH server of the inviting node, where
/// it may only run the enrollment; <see langword="null"/> when the node takes enrollments on its own TLS port.
/// </param>
public sealed record InvitationTicket(Guid Id, string Secret, NodeRole Role, DateTimeOffset ExpiresAtUtc, string? EnrollmentKey = null);
