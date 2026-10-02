using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Hashing;

/// <summary>
/// Computes a deterministic SHA-256 state hash from file entries.
/// </summary>
/// <remarks>
/// Entries are sorted by path (ordinal) and each contributes the line group
/// <c>path\nsha256\nsize\nsymlink\n</c> encoded as UTF-8, so the hash is independent of enumeration order and platform.
/// </remarks>
public sealed class DefaultStateHasher : IStateHasher
{
    /// <inheritdoc />
    /// <exception cref="ArgumentException">Two entries share the same path.</exception>
    public string ComputeHash(IReadOnlyList<FileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        string? previous = null;
        foreach (var entry in entries.OrderBy(entry => entry.Path.Replace('\\', '/'), StringComparer.Ordinal))
        {
            var path = entry.Path.Replace('\\', '/');
            if (path == previous)
            {
                throw new ArgumentException($"Duplicate path '{path}' in state entries.", nameof(entries));
            }

            previous = path;
            hash.AppendData(Encoding.UTF8.GetBytes($"{path}\n{entry.Sha256}\n{entry.SizeBytes}\n{(entry.IsSymlink ? 1 : 0)}\n"));
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the lowercase SHA-256 hex digest of a byte array.
    /// </summary>
    /// <param name="content">The content.</param>
    /// <returns>The digest.</returns>
    public static string Sha256Hex(ReadOnlySpan<byte> content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    /// <summary>
    /// Computes the lowercase SHA-256 hex digest of a file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The digest.</returns>
    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        var digest = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    /// <summary>
    /// How many leading bytes decide whether a file is text, as Git decides it: a NUL byte among them makes the file binary.
    /// </summary>
    public const int TEXT_PROBE_LENGTH = 8000;

    /// <summary>
    /// Computes the lowercase SHA-256 hex digest of a file with its line endings normalized: in a text file every CRLF counts as
    /// LF, so the same content checked out on Windows and on Linux has the same digest. A binary file is hashed as it is.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The digest.</returns>
    /// <remarks>
    /// This digest compares content, never transfers it: patches still carry and verify the exact bytes (<see cref="Sha256FileAsync"/>).
    /// </remarks>
    public static async Task<string> Sha256TextNormalizedFileAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 81920, useAsync: true);
        var buffer = new byte[81920];
        var read = await ReadAtLeastAsync(stream, buffer, TEXT_PROBE_LENGTH, cancellationToken);
        if (Array.IndexOf(buffer, (byte)0, 0, Math.Min(read, TEXT_PROBE_LENGTH)) >= 0)
        {
            stream.Position = 0;
            return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var pendingCarriageReturn = false;
        while (read > 0)
        {
            var start = 0;
            if (pendingCarriageReturn)
            {
                // A CR that ended the previous chunk is kept unless this chunk starts with the LF that completes a CRLF.
                if (buffer[0] != (byte)'\n')
                {
                    hash.AppendData("\r"u8);
                }

                pendingCarriageReturn = false;
            }

            for (var i = 0; i < read; i++)
            {
                if (buffer[i] != (byte)'\r')
                {
                    continue;
                }

                hash.AppendData(buffer, start, i - start);
                if (i + 1 == read)
                {
                    pendingCarriageReturn = true;
                }
                else if (buffer[i + 1] != (byte)'\n')
                {
                    hash.AppendData("\r"u8);
                }

                start = i + 1;
            }

            hash.AppendData(buffer, start, read - start);
            read = await stream.ReadAsync(buffer, cancellationToken);
        }

        if (pendingCarriageReturn)
        {
            hash.AppendData("\r"u8);
        }

        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Reads until the buffer holds at least the requested number of bytes or the stream ends.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="buffer">The buffer.</param>
    /// <param name="minimum">The number of bytes wanted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes read.</returns>
    private static async Task<int> ReadAtLeastAsync(Stream stream, byte[] buffer, int minimum, CancellationToken cancellationToken)
        => await stream.ReadAtLeastAsync(buffer, Math.Min(minimum, buffer.Length), throwOnEndOfStream: false, cancellationToken);
}
