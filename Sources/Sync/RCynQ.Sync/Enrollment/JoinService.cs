using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Security;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Joins this node to the node that wrote an invitation: checks the invitation, adds the inviting node and its Relays to
/// the chain, enrolls at the Relay with its ticket and at the inviting node with its own, then stores the link token and
/// lets the neighbours in. Every stage reports how it ended; the first failure stops the rest.
/// </summary>
public sealed class JoinService
{
    /// <summary>
    /// How long joining waits for a Relay that took this node but still has to let it through (with administrator rights,
    /// confirmed by a person at the Relay).
    /// </summary>
    public static readonly TimeSpan RelayAccessWait = TimeSpan.FromMinutes(3);

    /// <summary>The default pause between two tries to reach the inviting node through such a Relay.</summary>
    private static readonly TimeSpan _defaultRelayRetryDelay = TimeSpan.FromSeconds(5);

    /// <summary>The pause between two tries to reach the inviting node through a Relay that has not let this node through yet.</summary>
    private readonly TimeSpan _relayRetryDelay;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The import of the cards.</summary>
    private readonly NodeConfigurationTransfer _transfer;

    /// <summary>The links, which give the way to the Relay and to the inviting node.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The builder of the card of this node.</summary>
    private readonly LocalCardBuilder _cards;

    /// <summary>The channel of the enrollments.</summary>
    private readonly IEnrollmentChannel _channel;

    /// <summary>The tickets the Relays hand to this node.</summary>
    private readonly RelayTicketStore _relayTickets;

    /// <summary>The link token.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The writer of <c>authorized_keys</c>.</summary>
    private readonly PeerAccessService _access;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _time;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="JoinService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="transfer">The import of the cards.</param>
    /// <param name="links">The links.</param>
    /// <param name="cards">The builder of the card of this node.</param>
    /// <param name="channel">The channel of the enrollments.</param>
    /// <param name="relayTickets">The tickets the Relays hand to this node.</param>
    /// <param name="tokens">The link token.</param>
    /// <param name="access">The writer of <c>authorized_keys</c>.</param>
    /// <param name="time">The clock.</param>
    /// <param name="relayRetryDelay">
    /// The pause between two tries to reach the inviting node through a Relay that has not let this node through yet;
    /// <see langword="null"/> for five seconds.
    /// </param>
    public JoinService(
        ILocalizer localizer,
        NodeConfig node,
        NodeConfigurationTransfer transfer,
        IChainLinkRepository links,
        LocalCardBuilder cards,
        IEnrollmentChannel channel,
        RelayTicketStore relayTickets,
        LinkTokenProvider tokens,
        PeerAccessService access,
        TimeProvider time,
        TimeSpan? relayRetryDelay = null)
    {
        _relayRetryDelay = relayRetryDelay ?? _defaultRelayRetryDelay;
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _transfer = transfer ?? throw new ArgumentNullException(nameof(transfer));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _cards = cards ?? throw new ArgumentNullException(nameof(cards));
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _relayTickets = relayTickets ?? throw new ArgumentNullException(nameof(relayTickets));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _access = access ?? throw new ArgumentNullException(nameof(access));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <summary>
    /// Joins the node that wrote the invitation.
    /// </summary>
    /// <param name="text">The invitation as text.</param>
    /// <param name="configPath">The configuration file of this node: it gets the link token and the path of a new SSH key.</param>
    /// <param name="report">
    /// Told when a stage starts and how it ended, awaited before joining goes on, or <see langword="null"/>; the service
    /// passes these events on to the application while joining runs.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How every stage ended.</returns>
    public async Task<JoinResult> JoinAsync(string text, string configPath, Func<JoinProgressEvent, Task>? report = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var run = new JoinRun(_localizer, report);

        await run.BeginAsync(JoinStep.Invitation);
        Invitation invitation;
        try
        {
            invitation = InvitationCodec.Decode(text);
        }
        catch (FormatException ex)
        {
            return await run.FailAsync(JoinStep.Invitation, ex.Message);
        }

        if (Check(invitation) is { } problem)
        {
            return await run.FailAsync(JoinStep.Invitation, problem);
        }

        var inviter = invitation.Inviter;
        run.Inviter = inviter.Role;
        await run.EndAsync(JoinStep.Invitation, true, _localizer.Format("Join.InvitationOk", inviter.Role, inviter.NodeId.Value));

        await run.BeginAsync(JoinStep.Nodes);
        ExportedNodeConfiguration card;
        try
        {
            foreach (var other in invitation.Path.Append(inviter))
            {
                var imported = await _transfer.ImportAsync(other, cancellationToken);
                if (imported.Decision is not null)
                {
                    return await run.FailAsync(JoinStep.Nodes, imported.Message);
                }
            }

            card = await _cards.BuildAsync(configPath, cancellationToken);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or SyncBridgeException)
        {
            return await run.FailAsync(JoinStep.Nodes, _localizer.Describe(ex));
        }

        await run.EndAsync(JoinStep.Nodes, true, _localizer.Format("Join.NodesOk", invitation.Path.Count + 1));
        var request = new EnrollmentRequest(Guid.Empty, string.Empty, card, _localizer.Current.Code);
        var links = await _links.GetAllAsync(cancellationToken);

        NodeId? relayOnTheWay = null;
        NodeId? pendingRelay = null;
        if (invitation.RelayTicket is { } relayTicket && invitation.Path.Count == 1)
        {
            await run.BeginAsync(JoinStep.Relay);
            var relay = invitation.Path[0].NodeId;
            relayOnTheWay = relay;
            var (answer, error) = await EnrollAsync(links.Where(link => link.Peer.Id == relay).ToArray(), relayTicket, request, cancellationToken);

            if (answer is { Accepted: true, AccessPending: true })
            {
                // The way to the inviting node leads through this Relay, which lets this node through once a person
                // confirms it there: the next stage waits for that instead of giving up at once.
                pendingRelay = relay;
                await run.EndAsync(JoinStep.Relay, true, _localizer.Format("Join.RelayWaiting", relay.Value));
            }
            else
            {
                // A refusal does not stop the joining: on a second run with the same invitation the ticket of the Relay is
                // used up although the Relay knows this node already. If it does not, the next stage says so.
                await run.EndAsync(JoinStep.Relay, answer?.Accepted == true, answer?.Message ?? error!);
            }
        }

        await run.BeginAsync(JoinStep.Inviter);
        var onTheWay = invitation.Path.Select(other => other.NodeId).Append(inviter.NodeId).ToHashSet();
        var chain = links.Where(link => onTheWay.Contains(link.Peer.Id)).ToArray();
        var (response, failure) = await EnrollAsync(chain, invitation.Ticket, request, cancellationToken);
        var waitUntil = _time.GetUtcNow() + RelayAccessWait;
        while (pendingRelay is not null && response is null && _time.GetUtcNow() < waitUntil)
        {
            // Nothing reached the inviting node, so its ticket is still unused and the same enrollment can go again.
            await Task.Delay(_relayRetryDelay, _time, cancellationToken);
            (response, failure) = await EnrollAsync(chain, invitation.Ticket, request, cancellationToken);
        }

        if (response is null && pendingRelay is { } stillPending)
        {
            return await run.FailAsync(JoinStep.Inviter, $"{_localizer.Format("Join.RelayPending", stillPending.Value)} {failure}");
        }

        if (response is null || !response.Accepted)
        {
            return await run.FailAsync(JoinStep.Inviter, response?.Message ?? failure!);
        }

        // The ticket came over the network and goes into the next invitation of this node: it has to be a ticket for
        // the counterpart of this node, or the counterpart could not use it.
        if (inviter.Role == NodeRole.Relay &&
            InvitationCodec.IsValidTicket(response.RelayTicket) &&
            response.RelayTicket!.Role != _node.Role)
        {
            await _relayTickets.SaveAsync(inviter.NodeId.Value, response.RelayTicket, cancellationToken);
        }

        await run.EndAsync(
            JoinStep.Inviter,
            true,
            response.AccessPending ? $"{response.Message} {_localizer.Format("Join.InviterPending", inviter.NodeId.Value)}" : response.Message);

        await run.BeginAsync(JoinStep.Access);
        try
        {
            if (invitation.LinkToken is { } token)
            {
                await _tokens.ReplaceAsync(token, configPath, cancellationToken);
            }

            var access = await _access.ApplyAsync(cancellationToken);
            await run.EndAsync(JoinStep.Access, true, access.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return await run.FailAsync(JoinStep.Access, _localizer.Describe(ex));
        }

        if (relayOnTheWay is { } passed)
        {
            // The inviting node was reached through the Relay, so a Relay that refused its ticket knew this node already.
            run.Settle(JoinStep.Relay, _localizer.Format("Join.RelayKnown", passed.Value));
        }

        return run.Done(_localizer.Format("Join.Done", inviter.Role, inviter.NodeId.Value));
    }

    /// <summary>
    /// Delivers an enrollment and turns a failure to reach the node into its message.
    /// </summary>
    /// <param name="chain">The links to the node and the Relays on the way.</param>
    /// <param name="ticket">The ticket.</param>
    /// <param name="request">The enrollment without its ticket.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer, or <see langword="null"/> with the reason when the node could not be reached.</returns>
    private async Task<(EnrollmentResponse? Response, string? Error)> EnrollAsync(IReadOnlyList<ChainLinkConfig> chain, InvitationTicket ticket, EnrollmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return (await _channel.SendAsync(chain, ticket, request with { TicketId = ticket.Id, Secret = ticket.Secret }, cancellationToken), null);
        }
        catch (SyncBridgeException ex)
        {
            return (null, _localizer.Describe(ex));
        }
    }

    /// <summary>
    /// Checks that the invitation is meant for this node and can still be used.
    /// </summary>
    /// <param name="invitation">The invitation.</param>
    /// <returns>The reason it cannot be used, or <see langword="null"/>.</returns>
    private string? Check(Invitation invitation)
    {
        if (invitation.Inviter.NodeId == _node.Id)
        {
            return _localizer.Get("Join.Self");
        }

        if (invitation.Ticket.Role != _node.Role || !InvitationService.MayInvite(invitation.Inviter.Role, _node.Role))
        {
            return _localizer.Format("Join.WrongRole", invitation.Ticket.Role, _node.Role);
        }

        if (invitation.ProtocolVersion != ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION)
        {
            return _localizer.Format("Join.Protocol", invitation.ProtocolVersion, ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
        }

        return invitation.Ticket.ExpiresAtUtc <= _time.GetUtcNow() ? _localizer.Get("Join.Expired") : null;
    }

    /// <summary>
    /// One joining: the stages that ended so far, and who is told about each stage.
    /// </summary>
    private sealed class JoinRun
    {
        /// <summary>The localizer of the summary.</summary>
        private readonly ILocalizer _localizer;

        /// <summary>Told about every stage, or <see langword="null"/>.</summary>
        private readonly Func<JoinProgressEvent, Task>? _report;

        /// <summary>The stages that ended, in order.</summary>
        private readonly List<JoinStepResult> _steps = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="JoinRun"/> class.
        /// </summary>
        /// <param name="localizer">The localizer of the summary.</param>
        /// <param name="report">Told about every stage, or <see langword="null"/>.</param>
        public JoinRun(ILocalizer localizer, Func<JoinProgressEvent, Task>? report)
        {
            _localizer = localizer;
            _report = report;
        }

        /// <summary>Gets or sets the role of the inviting node, once the invitation is read.</summary>
        public NodeRole? Inviter { get; set; }

        /// <summary>
        /// Tells that a stage starts.
        /// </summary>
        /// <param name="step">The stage.</param>
        /// <returns>A task representing the operation.</returns>
        public Task BeginAsync(JoinStep step) => ReportAsync(new JoinProgressEvent(step, JoinStepState.Running, null, Inviter));

        /// <summary>
        /// Records how a stage ended and tells it.
        /// </summary>
        /// <param name="step">The stage.</param>
        /// <param name="succeeded">Whether it succeeded.</param>
        /// <param name="message">Its message.</param>
        /// <returns>A task representing the operation.</returns>
        public Task EndAsync(JoinStep step, bool succeeded, string message)
        {
            _steps.Add(new JoinStepResult(step, succeeded, message));
            return ReportAsync(new JoinProgressEvent(step, succeeded ? JoinStepState.Succeeded : JoinStepState.Failed, message, Inviter));
        }

        /// <summary>
        /// Ends the joining with a failed stage.
        /// </summary>
        /// <param name="step">The stage that failed.</param>
        /// <param name="message">Why it failed.</param>
        /// <returns>The result.</returns>
        public async Task<JoinResult> FailAsync(JoinStep step, string message)
        {
            await EndAsync(step, false, message);
            return new JoinResult(false, _localizer.Format("Join.Failed", message), _steps);
        }

        /// <summary>
        /// Records that a stage which failed was made good by the stages after it: the joining went through it after all.
        /// </summary>
        /// <param name="step">The stage.</param>
        /// <param name="message">What its result says now.</param>
        public void Settle(JoinStep step, string message)
        {
            var index = _steps.FindIndex(result => result.Step == step && !result.Succeeded);
            if (index >= 0)
            {
                _steps[index] = new JoinStepResult(step, true, message);
            }
        }

        /// <summary>
        /// Ends the joining with every stage done.
        /// </summary>
        /// <param name="message">The summary.</param>
        /// <returns>The result.</returns>
        public JoinResult Done(string message) => new(true, message, _steps);

        /// <summary>
        /// Tells an event, when somebody listens.
        /// </summary>
        /// <param name="progressEvent">The event.</param>
        /// <returns>A task representing the operation.</returns>
        private Task ReportAsync(JoinProgressEvent progressEvent) => _report is null ? Task.CompletedTask : _report(progressEvent);
    }
}
