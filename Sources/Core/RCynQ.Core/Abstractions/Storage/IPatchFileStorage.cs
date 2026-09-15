using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Storage;

/// <summary>
/// Stores and reads patch payloads (diff and bundle) on disk.
/// </summary>
/// <remarks>
/// The database stores patch metadata only; binary payloads live in the
/// patches directory of the application data paths.
/// </remarks>
public interface IPatchFileStorage
{
    /// <summary>
    /// Saves the diff and bundle payloads for the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="diff">The raw git diff payload. Must not be <see langword="null"/>.</param>
    /// <param name="bundle">The raw file bundle payload. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when both payloads have been written.</returns>
    /// <exception cref="ArgumentNullException">A payload is <see langword="null"/>.</exception>
    Task SaveAsync(Guid patchId, byte[] diff, byte[] bundle, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the diff payload of the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the diff payload bytes.</returns>
    /// <exception cref="SyncBridgeException">The patch payload is not stored.</exception>
    Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the bundle payload of the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the bundle payload bytes.</returns>
    /// <exception cref="SyncBridgeException">The patch payload is not stored.</exception>
    Task<byte[]> ReadBundleAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether payloads are stored for the given patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when the payload exists;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> ExistsAsync(Guid patchId, CancellationToken cancellationToken = default);
}
