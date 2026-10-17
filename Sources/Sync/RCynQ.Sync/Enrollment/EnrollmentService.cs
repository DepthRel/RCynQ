using System.Security.Cryptography;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Takes a node that enrolls with an invitation of this node: checks the ticket, uses it up, imports the card of the node
/// and lets it in. A Relay also hands out a ticket for the opposite endpoint.
/// </summary>
public sealed class EnrollmentService
{
    /// <summary>How long the ticket a Relay hands out for the opposite endpoint stays valid.</summary>
    public static readonly TimeSpan RelayTicketLifetime = TimeSpan.FromDays(7);

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The tickets.</summary>
    private readonly IEnrollmentTicketRepository _tickets;

    /// <summary>The import of the card.</summary>
    private readonly NodeConfigurationTransfer _transfer;

    /// <summary>The issuer of the ticket a Relay hands out.</summary>
    private readonly TicketIssuer _issuer;

    /// <summary>The writer of <c>authorized_keys</c>.</summary>
    private readonly PeerAccessService _access;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _time;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="EnrollmentService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="tickets">The tickets.</param>
    /// <param name="transfer">The import of the card.</param>
    /// <param name="issuer">The issuer of the ticket a Relay hands out.</param>
    /// <param name="access">The writer of <c>authorized_keys</c>.</param>
    /// <param name="time">The clock.</param>
    public EnrollmentService(
        ILocalizer localizer,
        NodeConfig node,
        IEnrollmentTicketRepository tickets,
        NodeConfigurationTransfer transfer,
        TicketIssuer issuer,
        PeerAccessService access,
        TimeProvider time)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Takes an enrolling node.
    /// </summary>
    /// <param name="request">The ticket and the card of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer; a refusal says why without telling whether the ticket exists.</returns>
    public async Task<EnrollmentResponse> EnrollAsync(EnrollmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _time.GetUtcNow();
        var ticket = await _tickets.GetAsync(request.TicketId, cancellationToken);
        if (ticket is null || !ticket.IsOpen(now) || !SecretMatches(ticket, request.Secret))
        {
            return Refuse("Enrollment.TicketRefused");
        }

        try
        {
            NodeConfigurationTransfer.Validate(request.Card);
        }
        catch (FormatException ex)
        {
            return new EnrollmentResponse(false, _localizer.Format("Enrollment.CardInvalid", ex.Message));
        }

        if (request.Card.Role != ticket.Role)
        {
            return new EnrollmentResponse(false, _localizer.Format("Enrollment.WrongRole", ticket.Role, request.Card.Role));
        }

        if (request.Card.ProtocolVersion != ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION)
        {
            return new EnrollmentResponse(false, _localizer.Format("Enrollment.Protocol", request.Card.ProtocolVersion?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "0.1", ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION));
        }

        // From here on the ticket is gone, whatever happens next: a second enrollment with it is refused even when this
        // one fails later.
        if (!await _tickets.TryUseAsync(ticket.Id, request.Card.NodeId.Value, now, cancellationToken))
        {
            return Refuse("Enrollment.TicketRefused");
        }

        var imported = await _transfer.ImportAsync(request.Card, cancellationToken);
        if (imported.Decision is not null)
        {
            // A Relay keeps several endpoints of a role side by side; an endpoint has one counterpart.
            if (_node.Role != NodeRole.Relay)
            {
                return new EnrollmentResponse(false, _localizer.Format("Enrollment.RoleTaken", request.Card.Role));
            }

            await _transfer.ImportAsync(request.Card, ImportDecision.AddHop, null, cancellationToken);
        }

        var relayTicket = _node.Role == NodeRole.Relay
            ? await _issuer.IssueAsync(request.Card.Role == NodeRole.Origin ? NodeRole.Workstation : NodeRole.Origin, RelayTicketLifetime, cancellationToken)
            : null;
        var access = await _access.ApplyAsync(cancellationToken);
        return new EnrollmentResponse(true, _localizer.Format("Enrollment.Accepted", _node.Id.Value), relayTicket, access.RequiresElevation);
    }

    /// <summary>
    /// Compares the presented secret with the stored hash in constant time.
    /// </summary>
    /// <param name="ticket">The stored ticket.</param>
    /// <param name="secret">The presented secret.</param>
    /// <returns><see langword="true"/> when they match.</returns>
    private static bool SecretMatches(EnrollmentTicket ticket, string? secret)
        => !string.IsNullOrEmpty(secret) && CryptographicOperations.FixedTimeEquals(TicketIssuer.Hash(secret), ticket.SecretHash);

    /// <summary>
    /// Builds a refusal.
    /// </summary>
    /// <param name="key">The localization key of the reason.</param>
    /// <returns>The answer.</returns>
    private EnrollmentResponse Refuse(string key) => new(false, _localizer.Get(key));
}
