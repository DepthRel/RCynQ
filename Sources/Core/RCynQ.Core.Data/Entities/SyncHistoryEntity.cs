namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a sync history entry (table <c>SyncHistory</c>).
/// </summary>
public sealed class SyncHistoryEntity
{
    /// <summary>Gets or sets the entry identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the time of the operation.</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Gets or sets the direction as its integer value.</summary>
    public int Direction { get; set; }

    /// <summary>Gets or sets the sender node identifier.</summary>
    public string SourceNode { get; set; } = string.Empty;

    /// <summary>Gets or sets the receiver node identifier.</summary>
    public string TargetNode { get; set; } = string.Empty;

    /// <summary>Gets or sets the patch identifier.</summary>
    public Guid PatchId { get; set; }

    /// <summary>Gets or sets the status as its integer value.</summary>
    public int Status { get; set; }

    /// <summary>Gets or sets the number of applied files.</summary>
    public int AppliedFileCount { get; set; }

    /// <summary>Gets or sets the number of conflicting files.</summary>
    public int ConflictCount { get; set; }

    /// <summary>Gets or sets optional details.</summary>
    public string? Details { get; set; }
}
