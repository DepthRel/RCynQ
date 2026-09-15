using RCynQ.Core.Ids;
using RCynQ.Core.State;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists snapshot records of node states.
/// </summary>
public interface ISnapshotRepository
{
    /// <summary>
    /// Creates a new snapshot record.
    /// </summary>
    /// <param name="snapshot">The snapshot to store. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the stored snapshot.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is <see langword="null"/>.</exception>
    Task<SnapshotInfo> CreateAsync(SnapshotInfo snapshot, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the snapshot with the given identifier.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the snapshot, or <see langword="null"/> when no
    /// snapshot exists with the identifier.
    /// </returns>
    Task<SnapshotInfo?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent snapshot of the given node.
    /// </summary>
    /// <param name="node">The node the snapshot belongs to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the most recent snapshot of the node, or
    /// <see langword="null"/> when the node has no snapshots.
    /// </returns>
    Task<SnapshotInfo?> GetLatestAsync(NodeId node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored snapshots, most recent first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the collection of snapshots.</returns>
    Task<IReadOnlyList<SnapshotInfo>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the snapshot with the given identifier.
    /// </summary>
    /// <param name="id">The snapshot identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when a snapshot was deleted;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
