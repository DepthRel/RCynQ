namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a ticket of an invitation (table <c>EnrollmentTickets</c>).
/// </summary>
public sealed class EnrollmentTicketEntity
{
    /// <summary>Gets or sets the ticket identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the role the enrolling node must have.</summary>
    public int Role { get; set; }

    /// <summary>Gets or sets the SHA-256 of the secret.</summary>
    public byte[] SecretHash { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets when the ticket was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets when the ticket stops being accepted.</summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>Gets or sets the public line of the one-time SSH key, or <see langword="null"/>.</summary>
    public string? EnrollmentKey { get; set; }

    /// <summary>Gets or sets when a node enrolled with the ticket, or <see langword="null"/>.</summary>
    public DateTimeOffset? UsedAtUtc { get; set; }

    /// <summary>Gets or sets the identifier of the node that enrolled, or <see langword="null"/>.</summary>
    public string? UsedBy { get; set; }

    /// <summary>Gets or sets when the ticket was revoked, or <see langword="null"/>.</summary>
    public DateTimeOffset? RevokedAtUtc { get; set; }
}
