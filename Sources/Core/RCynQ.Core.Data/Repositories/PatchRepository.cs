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
}
