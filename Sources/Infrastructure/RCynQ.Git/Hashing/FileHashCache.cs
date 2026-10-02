using System.Collections.Concurrent;
using RCynQ.Git.Paths;

namespace RCynQ.Git.Hashing;

/// <summary>
/// Remembers the SHA-256 digest of files, so a tree that is scanned again and again is not read again and again.
/// </summary>
/// <remarks>
/// <para>
/// The state of a node is computed on every status request, and each computation used to read every file of the working
/// tree and of the sync base. A digest is reused here while the size and the modification time of the file are what they
/// were when it was computed, which is how the index of Git decides that a file is unchanged.
/// </para>
/// <para>
/// A file that was modified within <see cref="RecentWriteWindow"/> before it was read is not remembered: a second write
/// within the resolution of the file system clock would leave the same size and time on other content. Such a file is
/// simply read again on the next scan, until it has rested.
/// </para>
/// </remarks>
public sealed class FileHashCache
{
    /// <summary>
    /// A file modified this recently before it was read is not remembered.
    /// </summary>
    public static readonly TimeSpan RecentWriteWindow = TimeSpan.FromSeconds(3);

    /// <summary>
    /// The number of remembered files above which the cache starts anew, so trees that come and go do not accumulate.
    /// </summary>
    public const int Capacity = 500_000;

    /// <summary>The remembered digests by full path.</summary>
    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(PathUtility.PlatformComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileHashCache"/> class.
    /// </summary>
    /// <param name="timeProvider">The clock.</param>
    public FileHashCache(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Gets the number of remembered files.
    /// </summary>
    public int Count => _entries.Count;

    /// <summary>
    /// Gets the lowercase SHA-256 hex digest of a file, reading the file only when it changed since the digest was
    /// remembered.
    /// </summary>
    /// <param name="file">The file, with the size and modification time of the moment it was listed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The digest.</returns>
    public async Task<string> GetSha256Async(FileInfo file, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        var path = file.FullName;
        var length = file.Length;
        var writtenAt = file.LastWriteTimeUtc;
        if (_entries.TryGetValue(path, out var known) && known.Length == length && known.WrittenAtUtc == writtenAt)
        {
            return known.Sha256;
        }

        var readAt = _timeProvider.GetUtcNow().UtcDateTime;
        var sha256 = await DefaultStateHasher.Sha256FileAsync(path, cancellationToken);

        // Looked at again after the read: a file that changed while it was read has no digest worth keeping.
        file.Refresh();
        if (file.Exists && file.Length == length && file.LastWriteTimeUtc == writtenAt && writtenAt + RecentWriteWindow < readAt)
        {
            if (_entries.Count >= Capacity)
            {
                _entries.Clear();
            }

            _entries[path] = new Entry(length, writtenAt, sha256);
        }
        else
        {
            _entries.TryRemove(path, out _);
        }

        return sha256;
    }

    /// <summary>
    /// A remembered digest and the state of the file it belongs to.
    /// </summary>
    /// <param name="Length">The size of the file in bytes.</param>
    /// <param name="WrittenAtUtc">The modification time of the file.</param>
    /// <param name="Sha256">The digest.</param>
    private sealed record Entry(long Length, DateTime WrittenAtUtc, string Sha256);
}
