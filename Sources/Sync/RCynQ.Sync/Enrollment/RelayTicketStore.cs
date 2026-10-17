using System.Text.Json;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Keeps the ticket a Relay handed to this endpoint when it enrolled there: the endpoint passes it on in its invitation
/// to its counterpart, so the counterpart can enroll at the same Relay. The ticket is a secret and is kept encrypted.
/// </summary>
public sealed class RelayTicketStore
{
    /// <summary>The start of the name of the secret; the identifier of the Relay follows.</summary>
    private const string SECRET_PREFIX = "relay-ticket:";

    /// <summary>The encrypted store.</summary>
    private readonly ISecretStore _secrets;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayTicketStore"/> class.
    /// </summary>
    /// <param name="secrets">The encrypted store.</param>
    public RelayTicketStore(ISecretStore secrets)
    {
        _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));
    }

    /// <summary>
    /// Keeps the ticket of a Relay, in place of an earlier one.
    /// </summary>
    /// <param name="relayId">The identifier of the Relay.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public Task SaveAsync(string relayId, InvitationTicket ticket, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relayId);
        ArgumentNullException.ThrowIfNull(ticket);

        return _secrets.SetAsync(SECRET_PREFIX + relayId, JsonSerializer.Serialize(ticket), cancellationToken);
    }

    /// <summary>
    /// Finds the ticket of a Relay that has not expired.
    /// </summary>
    /// <param name="relayId">The identifier of the Relay.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The ticket, or <see langword="null"/> when there is none or it expired.</returns>
    public async Task<InvitationTicket?> FindAsync(string relayId, DateTimeOffset nowUtc, CancellationToken cancellationToken = default)
    {
        var text = await _secrets.GetAsync(SECRET_PREFIX + relayId, cancellationToken);
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            var ticket = JsonSerializer.Deserialize<InvitationTicket>(text);
            return ticket is not null && ticket.ExpiresAtUtc > nowUtc ? ticket : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
