using System.Security.Cryptography;
using System.Text;
using RCynQ.Core.Abstractions;
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
}
