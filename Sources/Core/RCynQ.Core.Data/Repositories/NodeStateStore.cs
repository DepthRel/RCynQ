using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Ids;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="INodeStateStore"/> that keeps the state in the local node row.
/// </summary>
public sealed class NodeStateStore : INodeStateStore
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// The local node configuration.
    /// </summary>
    private readonly NodeConfig _localNode;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeStateStore"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    /// <param name="localNode">The local node, used to create the row on first save.</param>
    /// <param name="timeProvider">The clock.</param>
    public NodeStateStore(IDbContextFactory<RCynQDbContext> contextFactory, NodeConfig localNode, TimeProvider timeProvider)
    {
        _contextFactory = contextFactory;
        _localNode = localNode;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<SyncState> GetAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Nodes.AsNoTracking().SingleOrDefaultAsync(node => node.Id == _localNode.Id.Value, cancellationToken);
        return entity is null
            ? new SyncState(null, null, null, null)
            : new SyncState(entity.LocalStateHash, entity.SyncBaseHash, entity.LastSentAtUtc, entity.LastReceivedAtUtc);
    }

    /// <inheritdoc />
    public async Task SaveAsync(SyncState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var now = _timeProvider.GetUtcNow();
        var entity = await db.Nodes.FindAsync(new object[] { _localNode.Id.Value }, cancellationToken);
        if (entity is null)
        {
            entity = new NodeEntity();
            EntityMapper.Apply(_localNode, entity, now);
            db.Nodes.Add(entity);
        }

        entity.LocalStateHash = state.LocalStateHash;
        entity.SyncBaseHash = state.SyncBaseHash;
        entity.LastSentAtUtc = state.LastSentAtUtc;
        entity.LastReceivedAtUtc = state.LastReceivedAtUtc;
        entity.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);
    }
}
