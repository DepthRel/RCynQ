using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Storage;

/// <summary>
/// Stores the on-disk payloads of patches: the display diff, the content that peers upload and the content of
/// received patches that still wait for a decision.
/// </summary>
/// <remarks>
/// The database stores patch metadata only. Uploaded content is kept by its SHA-256 hash in a node-wide area until the
/// patch is committed, so an interrupted transfer can be resumed by any later attempt that needs the same files. On commit
/// the content moves into the directory of the patch and stays there until the patch is finished.
/// </remarks>
public interface IPatchFileStorage
{
    /// <summary>
    /// Saves the diff shown to the user for the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="diff">The diff bytes. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the diff has been written.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="diff"/> is <see langword="null"/>.</exception>
    Task SaveDiffAsync(Guid patchId, byte[] diff, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the diff shown to the user for the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the diff bytes.</returns>
    /// <exception cref="SyncBridgeException">The diff is not stored.</exception>
    Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a diff is stored for the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to <see langword="true"/> when the diff exists; otherwise <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether content with the given hash is available, either uploaded or already staged for the patch.
    /// </summary>
    /// <param name="patchId">The patch the content is needed for.</param>
    /// <param name="sha256">The SHA-256 hash (lowercase hex).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to <see langword="true"/> when the content is available.</returns>
    Task<bool> HasContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stores uploaded content after verifying that it has the announced hash and size limit.
    /// </summary>
    /// <param name="sha256">The SHA-256 hash (lowercase hex) the content must have.</param>
    /// <param name="content">The content stream. Must not be <see langword="null"/>.</param>
    /// <param name="expectedSize">The size in bytes the manifest announced; reading more than that fails.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the verified content is stored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The content is larger than announced or its hash differs; nothing is stored.</exception>
    Task SaveUploadAsync(string sha256, Stream content, long expectedSize, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the uploaded content of a patch into the directory of the patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sha256Hashes">The hashes the patch needs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when all content is staged.</returns>
    /// <exception cref="SyncBridgeException">Content is missing.</exception>
    Task StageContentAsync(Guid patchId, IReadOnlyCollection<string> sha256Hashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the staged content of a patch for reading.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sha256">The SHA-256 hash (lowercase hex).</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to a readable stream; the caller disposes it.</returns>
    /// <exception cref="SyncBridgeException">The content is not staged.</exception>
    Task<Stream> OpenContentAsync(Guid patchId, string sha256, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the staged content of a patch that is finished; the diff stays.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the content is deleted.</returns>
    Task DeleteContentAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes uploaded content that was not committed within the given time.
    /// </summary>
    /// <param name="olderThanUtc">Uploads last written before this moment are deleted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the stale uploads are deleted.</returns>
    Task PruneUploadsAsync(DateTimeOffset olderThanUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes everything stored for a patch: its diff and its content.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of bytes that were freed.</returns>
    Task<long> DeletePatchAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the patches that have stored data.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The identifiers of the patches with a diff or content on disk.</returns>
    Task<IReadOnlyList<Guid>> ListStoredPatchesAsync(CancellationToken cancellationToken = default);
}
