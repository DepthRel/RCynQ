using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="ISyncHistoryRepository"/>.
/// </summary>
public sealed class SyncHistoryRepository : ISyncHistoryRepository
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncHistoryRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public SyncHistoryRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<SyncHistoryEntry> AddAsync(SyncHistoryEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.SyncHistory.Add(new SyncHistoryEntity
        {
            Id = entry.Id,
            TimestampUtc = entry.TimestampUtc,
            Direction = (int)entry.Direction,
            SourceNode = entry.SourceNode.Value,
            TargetNode = entry.TargetNode.Value,
            PatchId = entry.PatchId,
            Status = (int)entry.Status,
            AppliedFileCount = entry.AppliedFileCount,
            ConflictCount = entry.ConflictCount,
            Details = entry.Details
        });
        await db.SaveChangesAsync(cancellationToken);
        return entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncHistoryEntry>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.SyncHistory.AsNoTracking().OrderByDescending(entry => entry.TimestampUtc).Take(count).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<SyncHistoryEntry>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.SyncHistory.AsNoTracking().OrderByDescending(entry => entry.TimestampUtc).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.SyncHistory.Where(entry => entry.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }
}
