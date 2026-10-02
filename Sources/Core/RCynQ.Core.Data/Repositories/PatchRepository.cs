using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Data.Mapping;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPatchRepository"/>.
/// </summary>
public sealed class PatchRepository : IPatchRepository
{
    /// <summary>
    /// The context factory.
    /// </summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public PatchRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task<PatchMetadata> UpsertAsync(PatchMetadata patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PatchEnvelopes.FindAsync(new object[] { patch.Id }, cancellationToken);
        if (entity is null)
        {
            entity = new PatchEnvelopeEntity();
            db.PatchEnvelopes.Add(entity);
        }

        EntityMapper.Apply(patch, entity);
        await db.SaveChangesAsync(cancellationToken);
        return patch;
    }

    /// <inheritdoc />
    public async Task<PatchMetadata?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PatchEnvelopes.AsNoTracking().SingleOrDefaultAsync(patch => patch.Id == id, cancellationToken);
        return entity is null ? null : EntityMapper.ToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PatchEnvelopes.AnyAsync(patch => patch.Id == id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.PatchEnvelopes.AsNoTracking()
            .OrderByDescending(patch => patch.CreatedAtUtc)
            .Take(count)
            .ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatchMetadata>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.PatchEnvelopes.AsNoTracking().OrderByDescending(patch => patch.CreatedAtUtc).ToListAsync(cancellationToken);
        return entities.Select(EntityMapper.ToDomain).ToArray();
    }

    /// <inheritdoc />
    public async Task<int> CountAsync(PatchDirection direction, PatchStatus status, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        return await db.PatchEnvelopes.CountAsync(patch => patch.Direction == (int)direction && patch.Status == (int)status, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Guid>> DeleteOlderThanAsync(DateTimeOffset createdBeforeUtc, IReadOnlyCollection<PatchStatus> keepStatuses, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keepStatuses);
        var keep = keepStatuses.Select(status => (int)status).ToArray();
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var ids = await db.PatchEnvelopes.AsNoTracking()
            .Where(patch => patch.CreatedAtUtc < createdBeforeUtc && !keep.Contains(patch.Status))
            .Select(patch => patch.Id)
            .ToListAsync(cancellationToken);
        if (ids.Count > 0)
        {
            await db.PatchEnvelopes.Where(patch => ids.Contains(patch.Id)).ExecuteDeleteAsync(cancellationToken);
        }

        return ids;
    }
}
