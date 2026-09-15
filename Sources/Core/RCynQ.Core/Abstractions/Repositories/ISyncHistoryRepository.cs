using RCynQ.Core.State;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists the node synchronization history.
/// </summary>
public interface ISyncHistoryRepository
{
    /// <summary>
    /// Adds a new sync history entry.
    /// </summary>
    /// <param name="entry">The history entry to add. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the stored history entry.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="entry"/> is <see langword="null"/>.</exception>
    Task<SyncHistoryEntry> AddAsync(SyncHistoryEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent sync history entries.
    /// </summary>
    /// <param name="count">The maximum number of entries to return. Must be non-negative.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to up to <paramref name="count"/> history entries,
    /// most recent first.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    Task<IReadOnlyList<SyncHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored sync history entries, most recent first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the collection of history entries.</returns>
    Task<IReadOnlyList<SyncHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the sync history entry with the given identifier.
    /// </summary>
    /// <param name="id">The history entry identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when an entry was deleted;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
