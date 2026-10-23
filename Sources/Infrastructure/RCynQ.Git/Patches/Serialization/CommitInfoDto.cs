namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// HTTP transfer object of one commit of a delivery of commits.
/// </summary>
public sealed class CommitInfoDto
{
    /// <summary>Gets or sets the full commit name.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Gets or sets the first line of the commit message.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Gets or sets the author as <c>Name &lt;email&gt;</c>.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Gets or sets the author date.</summary>
    public DateTimeOffset AuthoredAt { get; set; }
}
