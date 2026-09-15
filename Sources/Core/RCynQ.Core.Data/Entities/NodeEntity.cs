namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for the local node configuration and its synchronization state (table <c>Nodes</c>).
/// </summary>
public sealed class NodeEntity
{
    /// <summary>Gets or sets the node identifier (primary key).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the node role as its integer value.</summary>
    public int Role { get; set; }

    /// <summary>Gets or sets the repository path.</summary>
    public string? RepoPath { get; set; }

    /// <summary>Gets or sets the working directory path.</summary>
    public string? WorkdirPath { get; set; }

    /// <summary>Gets or sets the hash of the current local working tree state.</summary>
    public string? LocalStateHash { get; set; }

    /// <summary>Gets or sets the hash of the sync base state.</summary>
    public string? SyncBaseHash { get; set; }

    /// <summary>Gets or sets the time the last patch was sent.</summary>
    public DateTimeOffset? LastSentAtUtc { get; set; }

    /// <summary>Gets or sets the time the last patch was received.</summary>
    public DateTimeOffset? LastReceivedAtUtc { get; set; }

    /// <summary>Gets or sets the time the row was last updated.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
