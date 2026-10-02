namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for patch metadata (table <c>PatchEnvelopes</c>). Payload bytes are stored as files.
/// </summary>
public sealed class PatchEnvelopeEntity
{
    /// <summary>Gets or sets the patch identifier (primary key).</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the direction as its integer value.</summary>
    public int Direction { get; set; }

    /// <summary>Gets or sets the sender node identifier.</summary>
    public string SourceNode { get; set; } = string.Empty;

    /// <summary>Gets or sets the receiver node identifier.</summary>
    public string TargetNode { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation time.</summary>
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Gets or sets the sender's base state hash.</summary>
    public string BaseStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender's working tree hash.</summary>
    public string IncomingStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the status as its integer value.</summary>
    public int Status { get; set; }

    /// <summary>Gets or sets the time the patch reached a final state.</summary>
    public DateTimeOffset? AppliedAtUtc { get; set; }

    /// <summary>Gets or sets the JSON file manifest.</summary>
    public string ManifestJson { get; set; } = "[]";

    /// <summary>Gets or sets the JSON conflict list.</summary>
    public string ConflictsJson { get; set; } = "[]";

    /// <summary>Gets or sets the last error.</summary>
    public string? Error { get; set; }

    /// <summary>Gets or sets the envelope signature.</summary>
    public byte[]? Signature { get; set; }

    /// <summary>Gets or sets the name of the repository the patch belongs to, or <see langword="null"/>.</summary>
    public string? Repository { get; set; }
}
