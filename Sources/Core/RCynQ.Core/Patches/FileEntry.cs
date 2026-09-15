namespace RCynQ.Core.Patches;

/// <summary>
/// Describes the content state of a single file, used as input for deterministic
/// state hashing.
/// </summary>
/// <remarks>
/// Unlike <see cref="FileChange"/>, a <see cref="FileEntry"/> always describes an
/// existing file and therefore always carries a hash and size.
/// </remarks>
public sealed record FileEntry
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileEntry"/> record.
    /// </summary>
    /// <param name="path">
    /// The relative path of the file within the working directory. Must not be blank.
    /// </param>
    /// <param name="sha256">
    /// The SHA-256 hash (lowercase hex) of the file content. Must be 64 hex characters.
    /// </param>
    /// <param name="sizeBytes">The size of the file in bytes. Must be non-negative.</param>
    /// <param name="isSymlink">
    /// <see langword="true"/> when the file is a symbolic link; otherwise <see langword="false"/>.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> or <paramref name="sha256"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is blank or <paramref name="sha256"/> is malformed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sizeBytes"/> is negative.</exception>
    public FileEntry(string path, string sha256, long sizeBytes, bool isSymlink)
    {
        Path = ValidateText(path, nameof(path));
        Sha256 = ValidateHash(sha256);
        SizeBytes = ValidateSize(sizeBytes);
        IsSymlink = isSymlink;
    }

    /// <summary>
    /// Gets the relative path of the file within the working directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the file content.
    /// </summary>
    public string Sha256 { get; }

    /// <summary>
    /// Gets the size of the file in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Gets a value indicating whether the file is a symbolic link.
    /// </summary>
    public bool IsSymlink { get; }

    /// <summary>
    /// Validates that the given text is not <see langword="null"/> or blank.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="parameterName">The parameter name used in exceptions.</param>
    /// <returns>The trimmed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    private static string ValidateText(string? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Value must not be blank.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Validates that the given value is a well-formed SHA-256 hex string.
    /// </summary>
    /// <param name="value">The hash to validate.</param>
    /// <returns>The lowercase hash.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 hex characters.</exception>
    private static string ValidateHash(string? value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var hash = value.Trim().ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => (c < '0' || c > '9') && (c < 'a' || c > 'f')))
        {
            throw new ArgumentException("SHA-256 hash must be exactly 64 hexadecimal characters.", nameof(value));
        }

        return hash;
    }

    /// <summary>
    /// Validates that the given size is non-negative.
    /// </summary>
    /// <param name="value">The size in bytes.</param>
    /// <returns>The validated size.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    private static long ValidateSize(long value)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "File size must be non-negative.");
        }

        return value;
    }
}
