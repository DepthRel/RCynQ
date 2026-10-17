using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Entities;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Data.Repositories;

/// <summary>
/// EF Core repository of the tickets of invitations.
/// </summary>
public sealed class EnrollmentTicketRepository : IEnrollmentTicketRepository
{
    /// <summary>The context factory.</summary>
    private readonly IDbContextFactory<RCynQDbContext> _contextFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrollmentTicketRepository"/> class.
    /// </summary>
    /// <param name="contextFactory">The context factory.</param>
    public EnrollmentTicketRepository(IDbContextFactory<RCynQDbContext> contextFactory)
    {
        _contextFactory = contextFactory;
    }

    /// <inheritdoc />
    public async Task AddAsync(EnrollmentTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        db.EnrollmentTickets.Add(new EnrollmentTicketEntity
        {
            Id = ticket.Id,
            Role = (int)ticket.Role,
            SecretHash = ticket.SecretHash,
            CreatedAtUtc = ticket.CreatedAtUtc,
            ExpiresAtUtc = ticket.ExpiresAtUtc,
            EnrollmentKey = ticket.EnrollmentKey,
            UsedAtUtc = ticket.UsedAtUtc,
            UsedBy = ticket.UsedBy,
            RevokedAtUtc = ticket.RevokedAtUtc
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<EnrollmentTicket?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entity = await db.EnrollmentTickets.AsNoTracking().FirstOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);
        return entity is null ? null : ToTicket(entity);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<EnrollmentTicket>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.EnrollmentTickets.AsNoTracking().ToListAsync(cancellationToken);
        return entities.Select(ToTicket).OrderByDescending(ticket => ticket.CreatedAtUtc).ToArray();
    }

    /// <inheritdoc />
    public async Task<bool> TryUseAsync(Guid id, string nodeId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);

        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var changed = await db.EnrollmentTickets
            .Where(ticket => ticket.Id == id && ticket.UsedAtUtc == null && ticket.RevokedAtUtc == null && ticket.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(
                update => update.SetProperty(ticket => ticket.UsedAtUtc, nowUtc).SetProperty(ticket => ticket.UsedBy, nodeId),
                cancellationToken);
        return changed == 1;
    }

    /// <inheritdoc />
    public async Task<bool> RevokeAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        await using var db = await _contextFactory.CreateDbContextAsync(cancellationToken);
        var changed = await db.EnrollmentTickets
            .Where(ticket => ticket.Id == id && ticket.UsedAtUtc == null && ticket.RevokedAtUtc == null)
            .ExecuteUpdateAsync(update => update.SetProperty(ticket => ticket.RevokedAtUtc, nowUtc), cancellationToken);
        return changed == 1;
    }

    /// <summary>
    /// Maps an entity to the ticket.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <returns>The ticket.</returns>
    private static EnrollmentTicket ToTicket(EnrollmentTicketEntity entity)
        => new(
            entity.Id,
            (NodeRole)entity.Role,
            entity.SecretHash,
            entity.CreatedAtUtc,
            entity.ExpiresAtUtc,
            entity.EnrollmentKey,
            entity.UsedAtUtc,
            entity.UsedBy,
            entity.RevokedAtUtc);
}
