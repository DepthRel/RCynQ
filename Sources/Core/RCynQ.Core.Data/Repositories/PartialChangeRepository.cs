using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core repository of the choices of a part of the changes of files.
/// </summary>
public sealed class PartialChangeRepository : IPartialChangeRepository
{
    /// <summary>The separator of the indexes of the chosen changes.</summary>
    private const char BLOCK_SEPARATOR = ',';

    /// <summary>The context factory.</summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialChangeRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public PartialChangeRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory ?? throw new ArgumentNullException(nameof(contextFactory));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PartialChange>> GetAllAsync(Guid repositoryId, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.PartialChanges.AsNoTracking().Where(change => change.RepositoryId == repositoryId).ToListAsync(cancellationToken);
        return entities.Select(ToChange).OrderBy(change => change.Path, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<PartialChange?> GetAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PartialChanges.AsNoTracking().FirstOrDefaultAsync(change => change.RepositoryId == repositoryId && change.Path == path, cancellationToken);
        return entity is null ? null : ToChange(entity);
    }

    /// <inheritdoc />
    public async Task SaveAsync(PartialChange change, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(change);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.PartialChanges.FirstOrDefaultAsync(stored => stored.RepositoryId == change.RepositoryId && stored.Path == change.Path, cancellationToken);
        if (entity is null)
        {
            entity = new PartialChangeEntity { RepositoryId = change.RepositoryId, Path = change.Path };
            db.PartialChanges.Add(entity);
        }

        entity.BaseSha256 = change.BaseSha256;
        entity.WorkdirSha256 = change.WorkdirSha256;
        entity.Content = change.Content;
        entity.Blocks = change.Blocks is null ? null : string.Join(BLOCK_SEPARATOR, change.Blocks.Select(block => block.ToString(CultureInfo.InvariantCulture)));
        entity.ChosenAtUtc = change.ChosenAtUtc;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveAsync(Guid repositoryId, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var removed = await db.PartialChanges.Where(change => change.RepositoryId == repositoryId && change.Path == path).ExecuteDeleteAsync(cancellationToken);
        return removed > 0;
    }

    /// <summary>
    /// Maps an entity to the choice.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The choice.</returns>
    private static PartialChange ToChange(PartialChangeEntity entity)
    {
        IReadOnlyList<int>? blocks = entity.Blocks is null
            ? null
            : entity.Blocks.Split(BLOCK_SEPARATOR, StringSplitOptions.RemoveEmptyEntries).Select(block => int.Parse(block, CultureInfo.InvariantCulture)).ToArray();
        return new PartialChange(entity.RepositoryId, entity.Path, entity.BaseSha256, entity.WorkdirSha256, entity.Content, blocks, entity.ChosenAtUtc);
    }
}
