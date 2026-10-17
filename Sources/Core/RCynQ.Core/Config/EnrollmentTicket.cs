using RCynQ.Core.Enums;

namespace RCynQ.Core.Config;

/// <summary>
/// A ticket of an invitation as the inviting node keeps it: the hash of its secret instead of the secret, and its state.
/// </summary>
/// <param name="Id">The ticket identifier.</param>
/// <param name="Role">The role the enrolling node must have.</param>
/// <param name="SecretHash">The SHA-256 of the secret.</param>
/// <param name="CreatedAtUtc">When the ticket was created.</param>
/// <param name="ExpiresAtUtc">When it stops being accepted.</param>
/// <param name="EnrollmentKey">
/// The public line of the one-time SSH key that may log in to run the enrollment, or <see langword="null"/> when the node
/// takes enrollments on its own TLS port.
/// </param>
/// <param name="UsedAtUtc">When a node enrolled with it, or <see langword="null"/>.</param>
/// <param name="UsedBy">The identifier of that node, or <see langword="null"/>.</param>
/// <param name="RevokedAtUtc">When it was revoked, or <see langword="null"/>.</param>
public sealed record EnrollmentTicket(
    Guid Id,
    NodeRole Role,
    byte[] SecretHash,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    string? EnrollmentKey,
    DateTimeOffset? UsedAtUtc = null,
    string? UsedBy = null,
    DateTimeOffset? RevokedAtUtc = null)
{
    /// <summary>
    /// Tells whether the ticket is still accepted: unused, not revoked and not expired.
    /// </summary>
    /// <param name="nowUtc">The current time.</param>
    /// <returns><see langword="true"/> when a node may still enroll with it.</returns>
    public bool IsOpen(DateTimeOffset nowUtc) => UsedAtUtc is null && RevokedAtUtc is null && nowUtc < ExpiresAtUtc;
}
