using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// An invitation of this node as a list shows it; the secrets are never part of it.
/// </summary>
/// <param name="Id">The ticket identifier.</param>
/// <param name="Role">The role of the invited node.</param>
/// <param name="CreatedAtUtc">When the invitation was created.</param>
/// <param name="ExpiresAtUtc">When it stops being accepted.</param>
/// <param name="UsedAtUtc">When a node enrolled with it, or <see langword="null"/>.</param>
/// <param name="UsedBy">The identifier of the node that enrolled with it, or <see langword="null"/>.</param>
/// <param name="IsRevoked">Whether it was revoked.</param>
public sealed record InvitationDto(
    Guid Id,
    NodeRole Role,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset? UsedAtUtc,
    string? UsedBy,
    bool IsRevoked);
