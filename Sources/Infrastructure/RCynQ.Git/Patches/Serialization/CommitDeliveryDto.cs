namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// HTTP transfer object of the commits an envelope delivers; the bundle itself travels as the content of the session.
/// </summary>
public sealed class CommitDeliveryDto
{
    /// <summary>Gets or sets the branch the commits belong to.</summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>Gets or sets the parent of the first commit.</summary>
    public string BaseCommit { get; set; } = string.Empty;

    /// <summary>Gets or sets the commits on the first-parent chain, oldest first.</summary>
    public List<CommitInfoDto> Commits { get; set; } = new();

    /// <summary>Gets or sets the SHA-256 hash of the bundle.</summary>
    public string BundleSha256 { get; set; } = string.Empty;

    /// <summary>Gets or sets the size of the bundle in bytes.</summary>
    public long BundleSizeBytes { get; set; }
}
