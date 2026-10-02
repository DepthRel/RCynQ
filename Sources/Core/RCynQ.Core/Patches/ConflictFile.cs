using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// Describes a file whose local content conflicts with an incoming patch.
/// </summary>
public sealed record ConflictFile
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ConflictFile"/> record.
    /// </summary>
    /// <param name="path">The relative path of the file inside the working tree, using <c>/</c> separators.</param>
    /// <param name="localStateHash">The SHA-256 of the local content, or <see langword="null"/> when the file does not exist locally.</param>
    /// <param name="incomingStateHash">The SHA-256 of the incoming content, or <see langword="null"/> when the patch deletes the file.</param>
    /// <param name="detail">An optional explanation, as plain text or as an encoded message that is worded for the reader.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank.</exception>
    public ConflictFile(string path, string? localStateHash, string? incomingStateHash, string? detail = null)
    {
        Path = Guard.NotBlank(path, nameof(path));
        LocalStateHash = localStateHash;
        IncomingStateHash = incomingStateHash;
        Detail = detail;
    }

    /// <summary>
    /// Gets the relative path of the file inside the working tree.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the SHA-256 of the local content, or <see langword="null"/>.
    /// </summary>
    public string? LocalStateHash { get; }

    /// <summary>
    /// Gets the SHA-256 of the incoming content, or <see langword="null"/>.
    /// </summary>
    public string? IncomingStateHash { get; }

    /// <summary>
    /// Gets an optional explanation of the conflict.
    /// </summary>
    public string? Detail { get; }
}
