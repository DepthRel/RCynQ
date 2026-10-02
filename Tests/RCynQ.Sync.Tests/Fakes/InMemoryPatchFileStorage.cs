using System.Collections.Concurrent;
using System.Security.Cryptography;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Exceptions;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IPatchFileStorage"/> with the same rules as the file storage: uploads are verified against the announced hash and size,
/// staging moves them into the patch, and finished patches keep their diff only.
/// </summary>
public sealed class InMemoryPatchFileStorage : IPatchFileStorage
{
    /// <summary>Gets the stored diffs by patch.</summary>
    public ConcurrentDictionary<Guid, byte[]> Diffs { get; } = new();

    /// <summary>Gets the uploaded content that is not staged yet, by hash.</summary>
    public ConcurrentDictionary<string, byte[]> Uploads { get; } = new();

    /// <summary>Gets the staged content by patch and hash.</summary>
    public ConcurrentDictionary<(Guid PatchId, string Hash), byte[]> Staged { get; } = new();

    /// <inheritdoc />
    public Task SaveDiffAsync(Guid patchId, byte[] diff, CancellationToken cancellationToken = default)
    {
        Diffs[patchId] = diff;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
        => Task.FromResult(Diffs.TryGetValue(patchId, out var diff) ? diff : throw new SyncBridgeException("missing"));

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default) => Task.FromResult(Diffs.ContainsKey(patchId));

    /// <inheritdoc />
    public Task<bool> HasContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default)
        => Task.FromResult(Staged.ContainsKey((patchId, sha256)) || Uploads.ContainsKey(sha256));

    /// <inheritdoc />
    public async Task SaveUploadAsync(string sha256, Stream content, long expectedSize, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > expectedSize)
        {
            throw new FormatException("too large");
        }

        var bytes = buffer.ToArray();
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("hash mismatch");
        }

        Uploads[sha256] = bytes;
    }

    /// <inheritdoc />
    public Task StageContentAsync(Guid patchId, IReadOnlyCollection<string> sha256Hashes, CancellationToken cancellationToken = default)
    {
        foreach (var hash in sha256Hashes)
        {
            if (Staged.ContainsKey((patchId, hash)))
            {
                continue;
            }

            Staged[(patchId, hash)] = Uploads.TryRemove(hash, out var bytes) ? bytes : throw new SyncBridgeException("content missing");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Stream> OpenContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default)
        => Task.FromResult<Stream>(Staged.TryGetValue((patchId, sha256), out var bytes) ? new MemoryStream(bytes, writable: false) : throw new SyncBridgeException("content missing"));

    /// <inheritdoc />
    public Task DeleteContentAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        foreach (var key in Staged.Keys.Where(key => key.PatchId == patchId).ToArray())
        {
            Staged.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PruneUploadsAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task<long> DeletePatchAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        long freed = Diffs.TryRemove(patchId, out var diff) ? diff.Length : 0;
        foreach (var key in Staged.Keys.Where(key => key.PatchId == patchId).ToArray())
        {
            if (Staged.TryRemove(key, out var bytes))
            {
                freed += bytes.Length;
            }
        }

        return Task.FromResult(freed);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ListStoredPatchesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<Guid>>(Diffs.Keys.Concat(Staged.Keys.Select(key => key.PatchId)).Distinct().ToArray());
}
