using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Ids;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// Keeps peers and links in memory, the way the database keeps them.
/// </summary>
public sealed class InMemoryChainStore : IPeerRepository, IChainLinkRepository
{
    /// <summary>The peers by identifier.</summary>
    private readonly Dictionary<NodeId, PeerDescriptor> _peers = new();

    /// <summary>The links by identifier, in the order they were created.</summary>
    private readonly Dictionary<Guid, ChainLinkConfig> _links = new();

    /// <summary>Gets the stored peers.</summary>
    public IReadOnlyCollection<PeerDescriptor> Peers => _peers.Values;

    /// <summary>Gets the stored links.</summary>
    public IReadOnlyCollection<ChainLinkConfig> Links => _links.Values;

    /// <inheritdoc />
    public Task<PeerDescriptor> UpsertAsync(PeerDescriptor peer, CancellationToken cancellationToken = default)
    {
        _peers[peer.Id] = peer;
        return Task.FromResult(peer);
    }

    /// <inheritdoc />
    public Task<PeerDescriptor?> GetAsync(NodeId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_peers.GetValueOrDefault(id));

    /// <inheritdoc />
    Task<IReadOnlyList<PeerDescriptor>> IPeerRepository.GetAllAsync(CancellationToken cancellationToken)
        => Task.FromResult<IReadOnlyList<PeerDescriptor>>(_peers.Values.ToArray());

    /// <inheritdoc />
    public Task<bool> DeleteUnlinkedAsync(NodeId id, CancellationToken cancellationToken = default)
        => Task.FromResult(_links.Values.All(link => link.Peer.Id != id) && _peers.Remove(id));

    /// <inheritdoc />
    public Task<ChainLinkConfig> UpsertAsync(ChainLinkConfig link, CancellationToken cancellationToken = default)
    {
        _links[link.Id] = link;
        return Task.FromResult(link);
    }

    /// <inheritdoc />
    public Task<ChainLinkConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_links.GetValueOrDefault(id));

    /// <inheritdoc />
    public Task<IReadOnlyList<ChainLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ChainLinkConfig>>(_links.Values.ToArray());

    /// <inheritdoc />
    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(_links.Remove(id));
}
