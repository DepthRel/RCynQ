using RCynQ.Core.Config;

namespace RCynQ.Core.Abstractions.Repositories;

/// <summary>
/// Keeps the tickets of the invitations this node created.
/// </summary>
public interface IEnrollmentTicketRepository
{
    /// <summary>
    /// Stores a new ticket.
    /// </summary>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    Task AddAsync(EnrollmentTicket ticket, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a ticket.
    /// </summary>
    /// <param name="id">The ticket identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The ticket, or <see langword="null"/> when there is none.</returns>
    Task<EnrollmentTicket?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the tickets, the newest first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The tickets.</returns>
    Task<IReadOnlyList<EnrollmentTicket>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a ticket used, only when it is still open; two enrollments with one ticket cannot both win.
    /// </summary>
    /// <param name="id">The ticket identifier.</param>
    /// <param name="nodeId">The identifier of the enrolled node.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the ticket was open and is used now.</returns>
    Task<bool> TryUseAsync(Guid id, string nodeId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a ticket that is still open.
    /// </summary>
    /// <param name="id">The ticket identifier.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the ticket was open and is revoked now.</returns>
    Task<bool> RevokeAsync(Guid id, DateTimeOffset nowUtc, CancellationToken cancellationToken = default);
}
