using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// The version of a file that goes to the other node instead of the working file: the version of the other node with only
/// the changes the user chose.
/// </summary>
/// <remarks>
/// The version is built on one version of the other node; once the sync base holds another one, the choice no longer
/// fits and is dropped, otherwise sending it would take back on the other node what it got meanwhile.
/// </remarks>
public sealed record PartialChange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PartialChange"/> record.
    /// </summary>
    /// <param name="repositoryId">The local identifier of the repository.</param>
    /// <param name="path">The path relative to the working tree, with <c>/</c> separators.</param>
    /// <param name="baseSha256">The SHA-256 of the sync base file the choice was made on, or <see langword="null"/> when there was none.</param>
    /// <param name="workdirSha256">The SHA-256 of the working file when the choice was made.</param>
    /// <param name="content">The chosen version.</param>
    /// <param name="blocks">
    /// The indexes of the chosen changes in the diff of the base and working files of the choice, or <see langword="null"/>
    /// when the version was edited in an external tool.
    /// </param>
    /// <param name="chosenAtUtc">When the choice was made.</param>
    public PartialChange(Guid repositoryId, string path, string? baseSha256, string workdirSha256, byte[] content, IReadOnlyList<int>? blocks, DateTimeOffset chosenAtUtc)
    {
        RepositoryId = repositoryId;
        Path = Guard.NotBlank(path, nameof(path));
        BaseSha256 = baseSha256;
        WorkdirSha256 = Guard.NotBlank(workdirSha256, nameof(workdirSha256));
        Content = content ?? throw new ArgumentNullException(nameof(content));
        Blocks = blocks;
        ChosenAtUtc = chosenAtUtc.ToUniversalTime();
    }

    /// <summary>Gets the local identifier of the repository.</summary>
    public Guid RepositoryId { get; }

    /// <summary>Gets the path relative to the working tree, with <c>/</c> separators.</summary>
    public string Path { get; }

    /// <summary>Gets the SHA-256 of the sync base file the choice was made on, or <see langword="null"/> when there was none.</summary>
    public string? BaseSha256 { get; }

    /// <summary>Gets the SHA-256 of the working file when the choice was made.</summary>
    public string WorkdirSha256 { get; }

    /// <summary>Gets the chosen version.</summary>
    public byte[] Content { get; }

    /// <summary>
    /// Gets the indexes of the chosen changes in the diff of the base and working files of the choice, or
    /// <see langword="null"/> when the version was edited in an external tool.
    /// </summary>
    public IReadOnlyList<int>? Blocks { get; }

    /// <summary>Gets when the choice was made.</summary>
    public DateTimeOffset ChosenAtUtc { get; }
}
