namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// HTTP transfer object of a patch envelope: the manifest and the signature; file content travels in separate requests.
/// </summary>
public sealed class PatchEnvelopeDto
{
    /// <summary>Gets or sets the patch identifier.</summary>
    public Guid PatchId { get; set; }

    /// <summary>Gets or sets the sender node identifier.</summary>
    public string Sender { get; set; } = string.Empty;

    /// <summary>Gets or sets the receiver node identifier.</summary>
    public string Receiver { get; set; } = string.Empty;

    /// <summary>Gets or sets the creation time.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the sender's base state hash.</summary>
    public string BaseStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the sender's working tree hash.</summary>
    public string IncomingStateHash { get; set; } = string.Empty;

    /// <summary>Gets or sets the file changes.</summary>
    public List<FileChangeDto> Changes { get; set; } = new();

    /// <summary>Gets or sets the Base64 signature.</summary>
    public string? Signature { get; set; }

    /// <summary>
    /// Gets or sets the name of the repository the patch belongs to, or <see langword="null"/>.
    /// </summary>
    public string? Repository { get; set; }

    /// <summary>
    /// Gets or sets the commits the envelope delivers instead of files, or <see langword="null"/> for a patch of files.
    /// </summary>
    public CommitDeliveryDto? Commits { get; set; }
}
