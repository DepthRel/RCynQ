using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Ids;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISnapshotRepository"/>.
/// </summary>
public sealed class SnapshotRepository : ISnapshotRepository
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public SnapshotRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<SnapshotInfo> CreateAsync(SnapshotInfo snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.Snapshots.Add(new SnapshotEntity
        {
            Id = snapshot.Id,
            NodeId = snapshot.Node.Value,
            CreatedAtUtc = snapshot.CreatedAtUtc,
            StateHash = snapshot.StateHash,
            Label = snapshot.Label
        });
        await db.SaveChangesAsync(cancellationToken);
        return snapshot;
    }

    /// <inheritdoc />
    public async Task<SnapshotInfo?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Snapshots.AsNoTracking().SingleOrDefaultAsync(snapshot => snapshot.Id == id, cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<SnapshotInfo?> GetLatestAsync(NodeId node, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.Snapshots.AsNoTracking()
            .Where(snapshot => snapshot.NodeId == node.Value)
            .OrderByDescending(snapshot => snapshot.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SnapshotInfo>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.Snapshots.AsNoTracking().OrderByDescending(snapshot => snapshot.CreatedAtUtc).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.Snapshots.Where(snapshot => snapshot.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    /// <inheritdoc />
    public async Task<int> DeleteOlderThanAsync(DateTimeOffset createdBeforeUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var snapshots = await db.Snapshots.AsNoTracking().Select(snapshot => new { snapshot.Id, snapshot.NodeId, snapshot.CreatedAtUtc }).ToListAsync(cancellationToken);

        // The newest record of a node is what the node last built its base from; it stays whatever its age.
        var newest = snapshots.GroupBy(snapshot => snapshot.NodeId).Select(group => group.MaxBy(snapshot => snapshot.CreatedAtUtc)!.Id).ToHashSet();
        var outdated = snapshots.Where(snapshot => snapshot.CreatedAtUtc < createdBeforeUtc && !newest.Contains(snapshot.Id)).Select(snapshot => snapshot.Id).ToList();
        return outdated.Count == 0 ? 0 : await db.Snapshots.Where(snapshot => outdated.Contains(snapshot.Id)).ExecuteDeleteAsync(cancellationToken);
    }
}
