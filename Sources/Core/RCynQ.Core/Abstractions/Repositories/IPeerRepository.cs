using RCynQ.Core.Config;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Persists known peers, including peers imported without a chain link.
/// </summary>
public interface IPeerRepository
{
    /// <summary>
    /// Inserts or updates a peer.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored peer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="peer"/> is <see langword="null"/>.</exception>
    Task<PeerDescriptor> UpsertAsync(PeerDescriptor peer, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a peer by identifier.
    /// </summary>
    /// <param name="id">The peer identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The peer, or <see langword="null"/> when not found.</returns>
    Task<PeerDescriptor?> GetAsync(NodeId id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all peers ordered by identifier.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>All peers.</returns>
    Task<IReadOnlyList<PeerDescriptor>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Forgets a peer that no chain link leads to, together with the keys imported for it.
    /// </summary>
    /// <remarks>
    /// A peer that is replaced by another node must not stay known: its SSH key would keep its access to this node and
    /// its signing key would keep being accepted.
    /// </remarks>
    /// <param name="id">The peer identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when the peer was removed; <see langword="false"/> when it is unknown or a chain link
    /// still leads to it.
    /// </returns>
    Task<bool> DeleteUnlinkedAsync(NodeId id, CancellationToken cancellationToken = default);
}
