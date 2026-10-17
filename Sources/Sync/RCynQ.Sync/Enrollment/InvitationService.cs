using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Security;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Creates, lists and revokes the invitations of this node. An invitation carries the card of this node, the Relays the
/// invited node goes through, a ticket of this node, the ticket of the Relay when this node holds one, and the link
/// token for the counterpart.
/// </summary>
public sealed class InvitationService
{
    /// <summary>The longest lifetime of an invitation, in hours (a week).</summary>
    public const int MAX_LIFETIME_HOURS = 168;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The issuer of the tickets.</summary>
    private readonly TicketIssuer _issuer;

    /// <summary>The tickets.</summary>
    private readonly IEnrollmentTicketRepository _tickets;

    /// <summary>The links, whose Relays lie on the way of the invited node.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The builder of the card of this node.</summary>
    private readonly LocalCardBuilder _cards;

    /// <summary>The link token.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The tickets the Relays handed to this node.</summary>
    private readonly RelayTicketStore _relayTickets;

    /// <summary>The writer of <c>authorized_keys</c>, which takes the one-time keys.</summary>
    private readonly PeerAccessService _access;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _time;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="InvitationService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="issuer">The issuer of the tickets.</param>
    /// <param name="tickets">The tickets.</param>
    /// <param name="links">The links.</param>
    /// <param name="cards">The builder of the card of this node.</param>
    /// <param name="tokens">The link token.</param>
    /// <param name="relayTickets">The tickets the Relays handed to this node.</param>
    /// <param name="access">The writer of <c>authorized_keys</c>.</param>
    /// <param name="time">The clock.</param>
    public InvitationService(
        ILocalizer localizer,
        NodeConfig node,
        TicketIssuer issuer,
        IEnrollmentTicketRepository tickets,
        IChainLinkRepository links,
        LocalCardBuilder cards,
        LinkTokenProvider tokens,
        RelayTicketStore relayTickets,
        PeerAccessService access,
        TimeProvider time)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        _tickets = tickets ?? throw new ArgumentNullException(nameof(tickets));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _relayTickets = relayTickets ?? throw new ArgumentNullException(nameof(relayTickets));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Tells whether this node may invite a node of the role: a Relay invites either endpoint, an endpoint invites its
    /// counterpart.
    /// </summary>
    /// <param name="inviter">The role of the inviting node.</param>
    /// <param name="invited">The role of the invited node.</param>
    /// <returns><see langword="true"/> when the invitation makes sense.</returns>
    public static bool MayInvite(NodeRole inviter, NodeRole invited) => inviter switch
    {
        NodeRole.Relay => invited is NodeRole.Workstation or NodeRole.Origin,
        NodeRole.Workstation => invited == NodeRole.Origin,
        NodeRole.Origin => invited == NodeRole.Workstation,
        _ => false
    };

    /// <summary>
    /// Creates an invitation.
    /// </summary>
    /// <param name="request">The role of the invited node and the lifetime.</param>
    /// <param name="configPath">The configuration file of this node, which gets the path of an SSH key that is created.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The invitation.</returns>
    /// <exception cref="ArgumentException">The role or the lifetime does not fit.</exception>
    /// <exception cref="SyncBridgeException">The invitation cannot be made: no link token, no command line tool.</exception>
    public async Task<CreatedInvitation> CreateAsync(CreateInvitationRequest request, string configPath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!MayInvite(_node.Role, request.Role))
        {
            throw new ArgumentException(_localizer.Format("Invitation.RoleNotInvited", _node.Role, request.Role), nameof(request));
        }

        if (request.LifetimeHours is < 1 or > MAX_LIFETIME_HOURS)
        {
            throw new ArgumentException(_localizer.Format("Invitation.Lifetime", MAX_LIFETIME_HOURS), nameof(request));
        }

        var linkToken = _node.Role == NodeRole.Relay ? null : await _tokens.GetTokenAsync(cancellationToken);
        if (_node.Role != NodeRole.Relay && string.IsNullOrEmpty(linkToken))
        {
            throw new SyncBridgeException(_localizer.Get("Invitation.NoLinkToken"));
        }

        var notes = new List<string>();
        var card = await _cards.BuildAsync(configPath, cancellationToken);
        var path = await PathAsync(cancellationToken);
        var relayTicket = await RelayTicketAsync(path, notes, cancellationToken);
        var ticket = await _issuer.IssueAsync(request.Role, TimeSpan.FromHours(request.LifetimeHours), cancellationToken);
        PeerAccessResult access;
        try
        {
            access = await _access.ApplyAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Nobody gets this invitation, so its ticket must not stay open.
            await _tickets.RevokeAsync(ticket.Id, _time.GetUtcNow(), CancellationToken.None);
            throw;
        }

        var invitation = new Invitation(
            Invitation.CURRENT_VERSION, ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION, card, path, ticket, relayTicket, linkToken);
        var summary = new InvitationDto(ticket.Id, ticket.Role, _time.GetUtcNow(), ticket.ExpiresAtUtc, null, null, false);
        return new CreatedInvitation(InvitationCodec.Encode(invitation), summary, access.RequiresElevation, notes);
    }

    /// <summary>
    /// Lists the invitations of this node, the newest first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The invitations.</returns>
    public async Task<IReadOnlyList<InvitationDto>> ListAsync(CancellationToken cancellationToken = default)
        => (await _tickets.GetAllAsync(cancellationToken))
            .Select(ticket => new InvitationDto(ticket.Id, ticket.Role, ticket.CreatedAtUtc, ticket.ExpiresAtUtc, ticket.UsedAtUtc, ticket.UsedBy, ticket.RevokedAtUtc is not null))
            .ToArray();

    /// <summary>
    /// Revokes an open invitation and removes its one-time key from <c>authorized_keys</c>.
    /// </summary>
    /// <param name="id">The ticket identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the invitation was open and is revoked now.</returns>
    public async Task<bool> RevokeAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var revoked = await _tickets.RevokeAsync(id, _time.GetUtcNow(), cancellationToken);

        if (revoked)
        {
            await _access.ApplyAsync(cancellationToken);
        }

        return revoked;
    }

    /// <summary>
    /// Builds the cards of the Relays the invited node goes through, nearest to it first: the reverse of the order this
    /// node reaches them in.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The cards; empty for a Relay or a node without Relays.</returns>
    private async Task<IReadOnlyList<ExportedNodeConfiguration>> PathAsync(CancellationToken cancellationToken)
    {
        if (_node.Role == NodeRole.Relay)
        {
            return Array.Empty<ExportedNodeConfiguration>();
        }

        var relays = (await _links.GetAllAsync(cancellationToken))
            .Where(link => link.Peer.Role == NodeRole.Relay)
            .OrderBy(link => link.Priority)
            .ThenBy(link => link.Name, StringComparer.Ordinal)
            .Reverse()
            .Take(InvitationCodec.MAX_PATH)
            .ToArray();
        return relays.Select(link => CardOf(link, _time.GetUtcNow())).ToArray();
    }

    /// <summary>
    /// Finds the ticket of the Relay of the path that this node holds, and notes when the invited node has to be known
    /// to the Relays already.
    /// </summary>
    /// <param name="path">The cards of the Relays.</param>
    /// <param name="notes">The notes for the user.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The ticket, or <see langword="null"/>.</returns>
    private async Task<InvitationTicket?> RelayTicketAsync(IReadOnlyList<ExportedNodeConfiguration> path, List<string> notes, CancellationToken cancellationToken)
    {
        if (path.Count == 0)
        {
            return null;
        }

        if (path.Count > 1)
        {
            notes.Add(_localizer.Get("Invitation.SeveralRelays"));
            return null;
        }

        var ticket = await _relayTickets.FindAsync(path[0].NodeId.Value, _time.GetUtcNow(), cancellationToken);
        if (ticket is null)
        {
            notes.Add(_localizer.Format("Invitation.NoRelayTicket", path[0].NodeId.Value));
        }

        return ticket;
    }

    /// <summary>
    /// Builds the card of a Relay this node is linked to, with every address it knows the Relay by.
    /// </summary>
    /// <param name="link">The link to the Relay.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <returns>The card.</returns>
    private static ExportedNodeConfiguration CardOf(ChainLinkConfig link, DateTimeOffset nowUtc)
    {
        var peer = link.Peer;
        var addresses = link.Routes.Select(route => route.Ssh.Host)
            .Prepend(peer.Host)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(ExportedNodeConfiguration.MAX_ADDRESSES)
            .ToArray();
        return new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION,
            peer.Id,
            peer.Name,
            peer.Role,
            peer.SigningPublicKey,
            addresses[0],
            peer.SshUser,
            peer.SshPort,
            link.RemotePort,
            peer.SshPublicKey,
            nowUtc,
            peer.HostKeyFingerprint,
            peer.EndpointMode,
            peer.EndpointPort,
            peer.TlsFingerprint,
            addresses);
    }
}
