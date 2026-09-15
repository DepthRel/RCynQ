using RCynQ.Core.Ids;

namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a snapshot record (table <c>Snapshots</c>).
/// </summary>
public sealed class SnapshotEntity
{
    /// <summary>Gets or sets the snapshot identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the node the snapshot belongs to.</summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets the captured state hash.</summary>
    public string StateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the optional label.</summary>
    public string? Label { get; set; }
}
