using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Validation;

namespace RCynQ.Core.State;

/// <summary>
/// One entry of the synchronization history.
/// </summary>
public sealed record SyncHistoryEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SyncHistoryEntry"/> record.
    /// </summary>
    /// <param name="id">The entry identifier.</param>
    /// <param name="timestampUtc">The time of the operation.</param>
    /// <param name="direction">Whether the patch was sent or received.</param>
    /// <param name="sourceNode">The sender node.</param>
    /// <param name="targetNode">The receiver node.</param>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="status">The resulting status.</param>
    /// <param name="appliedFileCount">The number of applied files.</param>
    /// <param name="conflictCount">The number of conflicting files.</param>
    /// <param name="details">Optional details.</param>
    /// <exception cref="ArgumentException">An enum is not defined.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A count is negative.</exception>
    public SyncHistoryEntry(
        Guid id,
        DateTimeOffset timestampUtc,
        PatchDirection direction,
        NodeId sourceNode,
        NodeId targetNode,
        Guid patchId,
        PatchStatus status,
        int appliedFileCount,
        int conflictCount,
        string? details)
    {
        Id = id;
        TimestampUtc = timestampUtc.ToUniversalTime();
        Direction = Guard.Defined(direction, nameof(direction));
        SourceNode = sourceNode;
        TargetNode = targetNode;
        PatchId = patchId;
        Status = Guard.Defined(status, nameof(status));
        AppliedFileCount = Guard.NonNegative(appliedFileCount, nameof(appliedFileCount));
        ConflictCount = Guard.NonNegative(conflictCount, nameof(conflictCount));
        Details = details;
    }

    /// <summary>
    /// Gets the entry identifier.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the time of the operation.
    /// </summary>
    public DateTimeOffset TimestampUtc { get; }

    /// <summary>
    /// Gets whether the patch was sent or received.
    /// </summary>
    public PatchDirection Direction { get; }

    /// <summary>
    /// Gets the sender node.
    /// </summary>
    public NodeId SourceNode { get; }

    /// <summary>
    /// Gets the receiver node.
    /// </summary>
    public NodeId TargetNode { get; }

    /// <summary>
    /// Gets the patch identifier.
    /// </summary>
    public Guid PatchId { get; }

    /// <summary>
    /// Gets the resulting status.
    /// </summary>
    public PatchStatus Status { get; }

    /// <summary>
    /// Gets the number of applied files.
    /// </summary>
    public int AppliedFileCount { get; }

    /// <summary>
    /// Gets the number of conflicting files.
    /// </summary>
    public int ConflictCount { get; }

    /// <summary>
    /// Gets optional details.
    /// </summary>
    public string? Details { get; }
}
