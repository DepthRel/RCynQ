using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;

namespace RCynQ.Sync.Tests.Fakes;

/// <summary>
/// Keeps the tickets of invitations in memory with the rules of the database: a ticket is used or revoked once.
/// </summary>
public sealed class InMemoryEnrollmentTicketRepository : IEnrollmentTicketRepository
{
    /// <summary>The tickets by identifier.</summary>
    private readonly Dictionary<Guid, EnrollmentTicket> _tickets = new();

    /// <summary>The lock that makes using a ticket one step.</summary>
    private readonly object _gate = new();

    /// <inheritdoc />
    public Task AddAsync(EnrollmentTicket ticket, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _tickets.Add(ticket.Id, ticket);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<EnrollmentTicket?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_tickets.GetValueOrDefault(id));
        }
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<EnrollmentTicket>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<EnrollmentTicket>>(_tickets.Values.OrderByDescending(ticket => ticket.CreatedAtUtc).ToArray());
        }
    }

    /// <inheritdoc />
    public Task<bool> TryUseAsync(Guid id, string nodeId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(id, out var ticket) || !ticket.IsOpen(nowUtc))
            {
                return Task.FromResult(false);
            }

            _tickets[id] = ticket with { UsedAtUtc = nowUtc, UsedBy = nodeId };
            return Task.FromResult(true);
        }
    }

    /// <inheritdoc />
    public Task<bool> RevokeAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!_tickets.TryGetValue(id, out var ticket) || ticket.UsedAtUtc is not null || ticket.RevokedAtUtc is not null)
            {
                return Task.FromResult(false);
            }

            _tickets[id] = ticket with { RevokedAtUtc = nowUtc };
            return Task.FromResult(true);
        }
    }
}
