using RCynQ.Core.Enums;

namespace RCynQ.Git.Patches.Serialization;

/// <summary>
/// HTTP transfer object of a file change.
/// </summary>
public sealed class FileChangeDto
{
    /// <summary>Gets or sets the change kind.</summary>
    public ChangeKind Kind { get; set; }

    /// <summary>Gets or sets the relative path.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Gets or sets the incoming SHA-256.</summary>
    public string? Sha256 { get; set; }

    /// <summary>Gets or sets the incoming size.</summary>
    public long? SizeBytes { get; set; }

    /// <summary>Gets or sets a value indicating whether the entry is a symbolic link.</summary>
    public bool IsSymlink { get; set; }

    /// <summary>Gets or sets the SHA-256 of the file in the sender's sync base; absent for added files and from older senders.</summary>
    public string? BaseSha256 { get; set; }
}
