using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists patch metadata.
/// </summary>
/// <remarks>
/// Binary payloads are managed by <see cref="IPatchFileStorage"/>.
/// </remarks>
public interface IPatchRepository
{
    /// <summary>
    /// Inserts or updates patch metadata.
    /// </summary>
    /// <param name="patch">The metadata.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored metadata.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="patch"/> is <see langword="null"/>.</exception>
    Task<PatchMetadata> UpsertAsync(PatchMetadata patch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets patch metadata by identifier.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The metadata, or <see langword="null"/> when not found.</returns>
    Task<PatchMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether metadata with the given identifier exists.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the patch is known; otherwise <see langword="false"/>.</returns>
    Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent patches, newest first.
    /// </summary>
    /// <param name="count">The maximum number of records.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Up to <paramref name="count"/> records.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one page of the patches, newest first: the history is read part by part instead of all at once.
    /// </summary>
    /// <param name="skip">The number of the newest records to skip.</param>
    /// <param name="count">The maximum number of records.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Up to <paramref name="count"/> records that follow the first <paramref name="skip"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="skip"/> or <paramref name="count"/> is negative.</exception>
    Task<IReadOnlyList<PatchMetadata>> GetPageAsync(int skip, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all patches, newest first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>All records.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the patches of one direction that are in one state, without loading them.
    /// </summary>
    /// <param name="direction">The direction.</param>
    /// <param name="status">The status.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of such patches.</returns>
    Task<int> CountAsync(PatchDirection direction, PatchStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the patches created before a moment, except those in one of the given states.
    /// </summary>
    /// <param name="createdBeforeUtc">Patches created before this moment are deleted.</param>
    /// <param name="keepStatuses">The states of patches that are kept whatever their age.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The identifiers of the deleted patches.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="keepStatuses"/> is <see langword="null"/>.</exception>
    Task<IReadOnlyList<Guid>> DeleteOlderThanAsync(DateTimeOffset createdBeforeUtc, IReadOnlyCollection<PatchStatus> keepStatuses, CancellationToken cancellationToken = default);
}
