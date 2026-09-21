using RCynQ.Core.Enums;

namespace RCynQ.Core.Events;

/// <summary>
/// Event published when a patch changes status.
/// </summary>
/// <param name="PatchId">The patch identifier.</param>
/// <param name="Direction">Whether the patch was sent or received.</param>
/// <param name="Status">The new patch status.</param>
/// <param name="AppliedFileCount">The number of applied files.</param>
/// <param name="ConflictCount">The number of conflicting files.</param>
/// <param name="Message">An optional message.</param>
/// <param name="TimestampUtc">The event time.</param>
public sealed record PatchEvent(
    Guid PatchId,
    PatchDirection Direction,
    PatchStatus Status,
    int AppliedFileCount,
    int ConflictCount,
    string? Message,
    DateTimeOffset TimestampUtc);
