using RCynQ.Core.Enums;

namespace RCynQ.Core.Patches;

/// <summary>
/// Describes a single file-level change between a base state and an incoming state.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Sha256"/> and <see cref="SizeBytes"/> are <see langword="null"/>
/// for <see cref="ChangeKind.Deleted"/> changes because the file no longer exists
/// in the incoming state.
/// </para>
/// <para>
/// <see cref="BaseSha256"/> is the content the sender believed the receiver has for the path: the receiver applies the change
/// cleanly when its own file still has that content, and reports a conflict otherwise.
/// </para>
/// </remarks>
public sealed record FileChange
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FileChange"/> record.
    /// </summary>
    /// <param name="kind">The kind of change. Must be a defined <see cref="ChangeKind"/>.</param>
    /// <param name="path">
    /// The relative path of the file within the working directory. Must not be blank.
    /// </param>
    /// <param name="sha256">
    /// The SHA-256 hash (lowercase hex) of the incoming file content, or
    /// <see langword="null"/> for deleted files. When present, must be 64 hex characters.
    /// </param>
    /// <param name="sizeBytes">
    /// The size of the incoming file in bytes, or <see langword="null"/> for deleted files.
    /// When present, must be non-negative.
    /// </param>
    /// <param name="isSymlink">
    /// <see langword="true"/> when the file is a symbolic link; otherwise <see langword="false"/>.
    /// </param>
    /// <param name="baseSha256">
    /// The SHA-256 hash (lowercase hex) of the file in the sender's sync base, that is the content the sender believed the
    /// receiver has; <see langword="null"/> for added files and for patches of senders that do not report it.
    /// When present, must be 64 hex characters.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="path"/> is blank, <paramref name="sha256"/> or <paramref name="baseSha256"/> is malformed, or
    /// <paramref name="sizeBytes"/> is negative.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sizeBytes"/> is negative.</exception>
    public FileChange(ChangeKind kind, string path, string? sha256, long? sizeBytes, bool isSymlink, string? baseSha256 = null)
    {
        Kind = kind;
        Path = ValidatePath(path);
        Sha256 = ValidateHash(sha256);
        SizeBytes = ValidateSize(sizeBytes);
        IsSymlink = isSymlink;
        BaseSha256 = ValidateHash(baseSha256);
    }

    /// <summary>
    /// Gets the kind of change.
    /// </summary>
    public ChangeKind Kind { get; }

    /// <summary>
    /// Gets the relative path of the file within the working directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the incoming file content, or <see langword="null"/>
    /// for deleted files.
    /// </summary>
    public string? Sha256 { get; }

    /// <summary>
    /// Gets the size of the incoming file in bytes, or <see langword="null"/>
    /// for deleted files.
    /// </summary>
    public long? SizeBytes { get; }

    /// <summary>
    /// Gets a value indicating whether the file is a symbolic link.
    /// </summary>
    public bool IsSymlink { get; }

    /// <summary>
    /// Gets the SHA-256 hash of the file in the sender's sync base, or <see langword="null"/> for added files and for patches
    /// of senders that do not report it.
    /// </summary>
    public string? BaseSha256 { get; }

    /// <summary>
    /// Gets a value indicating whether the sender reported the content it built the change from.
    /// </summary>
    /// <remarks>
    /// An added file was built from "no file", which needs no hash. A modified or deleted file without <see cref="BaseSha256"/>
    /// comes from a sender of an older version; the receiver then falls back to its own sync base.
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasKnownBase => Kind == ChangeKind.Added || BaseSha256 is not null;

    /// <summary>
    /// Validates that the given relative path is not <see langword="null"/> or blank.
    /// </summary>
    /// <param name="value">The path to validate.</param>
    /// <returns>The trimmed path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    private static string ValidatePath(string? value)
    {
        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("File path must not be blank.", nameof(value));
        }

        return trimmed;
    }

    /// <summary>
    /// Validates that the given value is a well-formed SHA-256 hex string, when present.
    /// </summary>
    /// <param name="value">The hash to validate, or <see langword="null"/>.</param>
    /// <returns>The lowercase hash, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is not 64 lowercase hex characters.</exception>
    private static string? ValidateHash(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var hash = value.Trim().ToLowerInvariant();
        if (hash.Length != 64 || hash.Any(c => (c < '0' || c > '9') && (c < 'a' || c > 'f')))
        {
            throw new ArgumentException("SHA-256 hash must be exactly 64 hexadecimal characters.", nameof(value));
        }

        return hash;
    }

    /// <summary>
    /// Validates that the given size is non-negative, when present.
    /// </summary>
    /// <param name="value">The size in bytes, or <see langword="null"/>.</param>
    /// <returns>The validated size, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    private static long? ValidateSize(long? value)
    {
        if (value is null || value.Value >= 0)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "File size must be non-negative.");
    }
}
