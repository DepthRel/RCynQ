using RCynQ.Core.Config;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists chain link configurations together with their peer descriptors.
/// </summary>
public interface IChainLinkRepository
{
    /// <summary>
    /// Inserts or updates the given chain link, persisting its peer descriptor as well.
    /// </summary>
    /// <param name="link">The chain link configuration to persist. Must not be <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the stored chain link configuration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="link"/> is <see langword="null"/>.</exception>
    Task<ChainLinkConfig> UpsertAsync(ChainLinkConfig link, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the chain link with the given identifier, including its peer.
    /// </summary>
    /// <param name="id">The chain link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to the chain link configuration, or <see langword="null"/>
    /// when no link exists with the identifier.
    /// </returns>
    Task<ChainLinkConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all stored chain links ordered by priority.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the collection of chain links ordered by priority.</returns>
    Task<IReadOnlyList<ChainLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the chain link with the given identifier.
    /// </summary>
    /// <param name="id">The chain link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A task that resolves to <see langword="true"/> when a link was deleted;
    /// otherwise <see langword="false"/>.
    /// </returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
