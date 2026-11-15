namespace RCynQ.Core.Data.Entities;

/// <summary>
/// EF Core entity for a choice of a part of the changes of a file (table <c>PartialChanges</c>).
/// </summary>
public sealed class PartialChangeEntity
{
    /// <summary>Gets or sets the local identifier of the repository (part of the key).</summary>
    public Guid RepositoryId { get; set; }

    /// <summary>Gets or sets the path relative to the working tree (part of the key).</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the SHA-256 of the base file the choice was made on, or <see langword="null"/>.</summary>
    public string? BaseSha256 { get; set; }

    /// <summary>Gets or sets the SHA-256 of the working file the choice was made on.</summary>
    public string WorkdirSha256 { get; set; } = string.Empty;

    /// <summary>Gets or sets the chosen version.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();

    /// <summary>Gets or sets the indexes of the chosen changes separated by commas, or <see langword="null"/>.</summary>
    public string? Blocks { get; set; }

    /// <summary>Gets or sets when the choice was made.</summary>
    public DateTimeOffset ChosenAtUtc { get; set; }
}
