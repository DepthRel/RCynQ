using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IChainLinkRepository"/> and <see cref="IPeerRepository"/>.
/// </summary>
/// <remarks>
/// Upserting a link also upserts its peer. Deleting a link keeps the peer so imported keys are not lost.
/// </remarks>
public sealed class ChainLinkRepository : IChainLinkRepository, IPeerRepository
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainLinkRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="timeProvider">The clock.</param>
    public ChainLinkRepository(IDbContextFactory<RCynQDbContext> contextFactory, TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<ChainLinkConfig> UpsertAsync(ChainLinkConfig link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await UpsertPeerAsync(db, link.Peer, cancellationToken);

        var entity = await db.ChainLinks.FindAsync(new object[] { link.Id }, cancellationToken);
        if (entity is null)
        {
            entity = new ChainLinkEntity();
            db.ChainLinks.Add(entity);
        }

        EntityMapper.Apply(link, entity);
        await db.SaveChangesAsync(cancellationToken);
        return link;
    }

    /// <inheritdoc />
    public async Task<ChainLinkConfig?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.ChainLinks.AsNoTracking().Include(link => link.Peer).SingleOrDefaultAsync(link => link.Id == id, cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ChainLinkConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.ChainLinks.AsNoTracking()
            .Include(link => link.Peer)
            .OrderBy(link => link.Priority)
            .ThenBy(link => link.Name)
            .ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.ChainLinks.Where(link => link.Id == id).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <inheritdoc />
    public async Task<PeerDescriptor> UpsertAsync(PeerDescriptor peer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(peer);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        await UpsertPeerAsync(db, peer, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return peer;
    }

    /// <inheritdoc />
    async Task<PeerDescriptor?> IPeerRepository.GetAsync(NodeId id, CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Peers.AsNoTracking().SingleOrDefaultAsync(peer => peer.Id == id.Value, cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    async Task<IReadOnlyList<PeerDescriptor>> IPeerRepository.GetAllAsync(CancellationToken cancellationToken)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.Peers.AsNoTracking().OrderBy(peer => peer.Id).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteUnlinkedAsync(NodeId id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Peers
            .Where(peer => peer.Id == id.Value && !db.ChainLinks.Any(link => link.PeerId == peer.Id))
            .ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }

    /// <summary>
    /// Adds or updates a peer entity in the given context without saving.
    /// </summary>
    /// <param name="db">The context.</param>
    /// <param name="peer">The peer.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the entity is tracked.</returns>
    private async Task UpsertPeerAsync(RCynQDbContext db, PeerDescriptor peer, CancellationToken cancellationToken)
    {
        var entity = await db.Peers.FindAsync(new object[] { peer.Id.Value }, cancellationToken);
        if (entity is null)
        {
            entity = new PeerEntity();
            db.Peers.Add(entity);
        }

        EntityMapper.Apply(peer, entity, _timeProvider.GetUtcNow());
    }
}
