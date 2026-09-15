using RCynQ.Core.Config;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists node configuration records.
/// </summary>
public interface INodeRepository
{
    /// <summary>
    /// Inserts or updates the given node configuration.
    /// </summary>
    /// <param name="node">The node configuration to persist. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the stored node configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    Task<NodeConfig> UpsertAsync(NodeConfig node, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the node configuration with the given identifier.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the node configuration, or <see langword="null"/>
    /// when no node exists with the identifier.
    /// </returns>
    Task<NodeConfig?> GetAsync(NodeId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored node configurations.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the collection of node configurations.</returns>
    Task<IReadOnlyList<NodeConfig>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the node configuration with the given identifier.
    /// </summary>
    /// <param name="id">The node identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when a node was deleted;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> DeleteAsync(NodeId id, CancellationToken cancellationToken = default);
}
