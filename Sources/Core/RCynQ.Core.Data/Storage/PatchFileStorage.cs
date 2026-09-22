using System.Security.Cryptography;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Core.Data.Storage;

/// <summary>
/// Stores patch payloads as files below the patches directory.
/// </summary>
/// <remarks>
/// <para>
/// Layout: <c>&lt;patches&gt;/&lt;id&gt;/patch.diff</c> is the diff shown to the user; <c>&lt;patches&gt;/&lt;id&gt;/content/&lt;sha256&gt;</c>
/// is the staged content of a received patch that is not finished yet; <c>&lt;patches&gt;/uploads/&lt;sha256&gt;</c> is content
/// a peer uploaded for a patch that is not committed yet. Uploads live in one node-wide area keyed by hash, so an
/// interrupted transfer is resumed by whichever later attempt needs the same files.
/// </para>
/// <para>
/// Hashes become file names, so every hash is validated as 64 lowercase hex characters before it touches a path.
/// </para>
/// </remarks>
public sealed class PatchFileStorage : IPatchFileStorage
{
    /// <summary>
    /// The diff file name.
    /// </summary>
    public const string DiffFileName = "patch.diff";

    /// <summary>
    /// The name of the directory with the staged content of a patch.
    /// </summary>
    public const string ContentDirectoryName = "content";

    /// <summary>
    /// The name of the node-wide directory with uploaded content.
    /// </summary>
    public const string UploadsDirectoryName = "uploads";

    /// <summary>
    /// The buffer size of content copies.
    /// </summary>
    private const int BufferSize = 81920;

    /// <summary>
    /// The application data factory.
    /// </summary>
    private readonly IAppDataFactory _appDataFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFileStorage"/> class.
    /// </summary>
    /// <param name="appDataFactory">The application data factory.</param>
    public PatchFileStorage(IAppDataFactory appDataFactory)
    {
        _appDataFactory = appDataFactory;
    }

    /// <inheritdoc />
    public async Task SaveDiffAsync(Guid patchId, byte[] diff, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(diff);
        var directory = GetPatchDirectory(patchId);
        Directory.CreateDirectory(directory);
        await FilePermissions.WriteAtomicallyAsync(Path.Combine(directory, DiffFileName), diff, ownerOnly: true, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(GetPatchDirectory(patchId), DiffFileName);
        if (!File.Exists(path))
        {
            throw new SyncBridgeException(Texts.Format("Patch.PayloadMissing", DiffFileName, patchId));
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default)
        => Task.FromResult(File.Exists(Path.Combine(GetPatchDirectory(patchId), DiffFileName)));

    /// <inheritdoc />
    public Task<bool> HasContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default)
    {
        var hash = ValidateHash(sha256);
        return Task.FromResult(File.Exists(StagedPath(patchId, hash)) || File.Exists(UploadPath(hash)));
    }

    /// <inheritdoc />
    public async Task SaveUploadAsync(string sha256, Stream content, long expectedSize, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var hash = ValidateHash(sha256);
        var uploads = UploadsDirectory();
        Directory.CreateDirectory(uploads);
        var temp = Path.Combine(uploads, $"{hash}.{Guid.NewGuid():N}.part");
        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long total = 0;
            await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > expectedSize)
                    {
                        throw new FormatException(Texts.Format("Upload.TooLarge", hash, expectedSize));
                    }

                    hasher.AppendData(buffer, 0, read);
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            if (!string.Equals(Convert.ToHexString(hasher.GetHashAndReset()), hash, StringComparison.OrdinalIgnoreCase))
            {
                throw new FormatException(Texts.Format("Upload.HashMismatch", hash));
            }

            FilePermissions.RestrictFileToOwner(temp);
            File.Move(temp, UploadPath(hash), overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <inheritdoc />
    public Task StageContentAsync(Guid patchId, IReadOnlyCollection<string> sha256Hashes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256Hashes);
        var directory = ContentDirectory(patchId);
        Directory.CreateDirectory(directory);
        foreach (var sha256 in sha256Hashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hash = ValidateHash(sha256);
            var target = StagedPath(patchId, hash);
            if (File.Exists(target))
            {
                continue;
            }

            var upload = UploadPath(hash);
            if (!File.Exists(upload))
            {
                throw new SyncBridgeException(Texts.Format("Patch.ContentMissing", hash, patchId));
            }

            File.Move(upload, target, overwrite: true);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<Stream> OpenContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default)
    {
        var path = StagedPath(patchId, ValidateHash(sha256));
        if (!File.Exists(path))
        {
            throw new SyncBridgeException(Texts.Format("Patch.ContentMissing", sha256, patchId));
        }

        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true));
    }

    /// <inheritdoc />
    public Task DeleteContentAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        var directory = ContentDirectory(patchId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task PruneUploadsAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default)
    {
        var uploads = UploadsDirectory();
        if (!Directory.Exists(uploads))
        {
            return Task.CompletedTask;
        }

        foreach (var file in Directory.EnumerateFiles(uploads))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(file) < olderThanUtc.UtcDateTime)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // An upload in use is pruned by a later call.
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets the directory of a patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <returns>The directory path.</returns>
    public string GetPatchDirectory(Guid patchId)
        => Path.Combine(_appDataFactory.Create().PatchesDirectory, patchId.ToString("N"));

    /// <summary>
    /// Gets the directory with the staged content of a patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <returns>The directory path.</returns>
    private string ContentDirectory(Guid patchId) => Path.Combine(GetPatchDirectory(patchId), ContentDirectoryName);

    /// <summary>
    /// Gets the path of staged content.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="hash">The validated hash.</param>
    /// <returns>The file path.</returns>
    private string StagedPath(Guid patchId, string hash) => Path.Combine(ContentDirectory(patchId), hash);

    /// <summary>
    /// Gets the node-wide directory with uploaded content.
    /// </summary>
    /// <returns>The directory path.</returns>
    private string UploadsDirectory() => Path.Combine(_appDataFactory.Create().PatchesDirectory, UploadsDirectoryName);

    /// <summary>
    /// Gets the path of uploaded content.
    /// </summary>
    /// <param name="hash">The validated hash.</param>
    /// <returns>The file path.</returns>
    private string UploadPath(string hash) => Path.Combine(UploadsDirectory(), hash);

    /// <summary>
    /// Validates a hash that becomes a file name.
    /// </summary>
    /// <param name="sha256">The hash.</param>
    /// <returns>The lowercase hash.</returns>
    /// <exception cref="ArgumentException">The value is not 64 hexadecimal characters.</exception>
    private static string ValidateHash(string sha256)
    {
        var hash = sha256?.Trim().ToLowerInvariant();
        if (hash is null || hash.Length != 64 || hash.Any(c => (c < '0' || c > '9') && (c < 'a' || c > 'f')))
        {
            throw new ArgumentException("A SHA-256 hash must be exactly 64 hexadecimal characters.", nameof(sha256));
        }

        return hash;
    }
}
