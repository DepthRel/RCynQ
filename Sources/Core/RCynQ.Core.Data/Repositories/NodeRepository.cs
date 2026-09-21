using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="INodeRepository"/>.
/// </summary>
public sealed class NodeRepository : INodeRepository
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
    /// Initializes a new instance of the <see cref="NodeRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="timeProvider">The clock.</param>
    public NodeRepository(IDbContextFactory<RCynQDbContext> contextFactory, TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<NodeConfig> UpsertAsync(NodeConfig node, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Nodes.FindAsync(new object[] { node.Id.Value }, cancellationToken);
        if (entity is null)
        {
            entity = new NodeEntity();
            db.Nodes.Add(entity);
        }

        EntityMapper.Apply(node, entity, _timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return node;
    }

    /// <inheritdoc />
    public async Task<NodeConfig?> GetAsync(NodeId id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Nodes.AsNoTracking().SingleOrDefaultAsync(node => node.Id == id.Value, cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NodeConfig>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.Nodes.AsNoTracking().OrderBy(node => node.Id).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(NodeId id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var deleted = await db.Nodes.Where(node => node.Id == id.Value).ExecuteDeleteAsync(cancellationToken);
        return deleted > 0;
    }
}
