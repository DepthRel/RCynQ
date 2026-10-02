using System.Net;
using System.Text.Json;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Security;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Reachability;

/// <summary>
/// Default <see cref="IEndpointProbe"/>: tells the state of the link to the counterpart endpoint by pinging it the way
/// a patch travels.
/// </summary>
/// <remarks>
/// <para>
/// The ping opens the same path a patch takes (the SSH forward through the mediators, with their pinned host keys, and
/// the pinned TLS certificate of a peer that runs no SSH server) and asks the peer whether it would take a patch from
/// this node. The outcome tells apart what a plain connection attempt cannot: no path to the peer, a path with nobody
/// answering at its end, and a peer that answers but refuses this node.
/// </para>
/// <para>
/// A ping takes a second or more, so a request for the state does not wait for it. It gets the last outcome, or
/// "being checked" when there is none, and a ping is started behind it when the last outcome is older than
/// <see cref="EndpointPingOptions.CacheTime"/>. The outcome of that ping is published as a link state event, which is
/// how the application learns it. Nothing is pinged while nobody asks.
/// </para>
/// </remarks>
public sealed class EndpointPinger : IEndpointProbe, IDisposable
{
    /// <summary>The JSON options of the peer API.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>Opens the path to the peer.</summary>
    private readonly PeerLinkFactory _links;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The contacts the node has had from its peers.</summary>
    private readonly PeerContactRegistry _contacts;

    /// <summary>The event publisher.</summary>
    private readonly IEventPublisher _events;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>How the link is checked.</summary>
    private readonly EndpointPingOptions _options;

    /// <summary>Guards the last outcome and the running ping.</summary>
    private readonly object _sync = new();

    /// <summary>One ping at a time.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Ends the pings that run behind a request when the service stops.</summary>
    private readonly CancellationTokenSource _shutdown = new();

    /// <summary>The outcome of the last ping, or <see langword="null"/> when there was none.</summary>
    private LinkState? _last;

    /// <summary>Whether a ping runs behind a request.</summary>
    private bool _pinging;

    /// <summary>Set once the pinger is disposed.</summary>
    private int _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndpointPinger"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="links">Opens the path to the peer; the pinger disposes it.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="contacts">The contacts the node has had from its peers.</param>
    /// <param name="events">The event publisher.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="options">How the link is checked.</param>
    public EndpointPinger(
        ILocalizer localizer,
        NodeConfig node,
        PeerLinkFactory links,
        LinkTokenProvider tokens,
        PeerContactRegistry contacts,
        IEventPublisher events,
        TimeProvider timeProvider,
        EndpointPingOptions options)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        _contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <inheritdoc />
    public async Task<LinkState?> GetStateAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(chain);
        if (!LeadsToCounterpart(link, chain))
        {
            return null;
        }

        if (_options.WaitForResult)
        {
            return WithContact(await PingAsync(link, chain, cancellationToken), link);
        }

        LinkState? known;
        lock (_sync)
        {
            known = _last is { } last && last.LinkId == link.Id ? last : null;
            var fresh = known is not null && _timeProvider.GetUtcNow() - known.UpdatedAtUtc < _options.CacheTime;
            if (!fresh && !_pinging)
            {
                _pinging = true;
                _ = Task.Run(() => PingBehindAsync(link, chain));
            }
        }

        return WithContact(
            known ?? new LinkState(link.Id, link.Name, link.Transport, LinkStatus.Starting, _localizer.Format("Ping.Checking", link.Peer.Id), _timeProvider.GetUtcNow()),
            link);
    }

    /// <summary>
    /// Pings the counterpart endpoint of a chain now and waits for the outcome.
    /// </summary>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The state of the link to the counterpart endpoint, or <see langword="null"/> when the chain has no such link
    /// (a mediator, or a node whose chain is not set up yet).
    /// </returns>
    public async Task<LinkState?> PingAsync(IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var link = chain.FirstOrDefault(candidate => LeadsToCounterpart(candidate, chain));
        return link is null ? null : WithContact(await PingAsync(link, chain, cancellationToken), link);
    }

    /// <summary>
    /// Ends the pings that run behind a request and releases the connections.
    /// </summary>
    public void Dispose()
    {
        // The container that offers the pinger under two contracts disposes it once for each.
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _shutdown.Cancel();
        _shutdown.Dispose();
        _gate.Dispose();
        _links.Dispose();
    }

    /// <summary>
    /// Tells whether a link leads to the endpoint this node exchanges patches with: the last link of the chain of a
    /// Source or a Dest, when its peer has the opposite role.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="chain">All links of the node.</param>
    /// <returns><see langword="true"/> when the state of the link comes from a ping.</returns>
    private bool LeadsToCounterpart(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain)
    {
        if (!_node.ParticipatesInPatchChannel || link.Peer.Role != (_node.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source))
        {
            return false;
        }

        // The same order the route of a patch is resolved in.
        var endpoint = chain.OrderBy(candidate => candidate.Priority).ThenBy(candidate => candidate.Name, StringComparer.Ordinal).LastOrDefault();
        return endpoint is not null && endpoint.Id == link.Id;
    }

    /// <summary>
    /// Runs a ping behind a request for the state; whatever happens, the request that started it is long answered.
    /// </summary>
    /// <param name="link">The link to the counterpart endpoint.</param>
    /// <param name="chain">All links of the node.</param>
    /// <returns>A task that completes when the ping is done.</returns>
    private async Task PingBehindAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain)
    {
        try
        {
            LinkState state;
            try
            {
                state = await PingAndKeepAsync(link, chain, _shutdown.Token);
            }
            finally
            {
                // Before the outcome is published: whoever reacts to it by asking again must be able to start a new ping.
                lock (_sync)
                {
                    _pinging = false;
                }
            }

            await PublishAsync(state, link, _shutdown.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
        {
            // The service is stopping.
        }
    }

    /// <summary>
    /// Pings the peer of a link, keeps the outcome and publishes it.
    /// </summary>
    /// <param name="link">The link to the counterpart endpoint.</param>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    private async Task<LinkState> PingAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken)
    {
        var state = await PingAndKeepAsync(link, chain, cancellationToken);
        await PublishAsync(state, link, cancellationToken);
        return state;
    }

    /// <summary>
    /// Pings the peer of a link and keeps the outcome as the last one.
    /// </summary>
    /// <param name="link">The link to the counterpart endpoint.</param>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    private async Task<LinkState> PingAndKeepAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var state = await PingOnceAsync(link, chain, cancellationToken);
            lock (_sync)
            {
                _last = state;
            }

            return state;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Publishes an outcome, so the application shows it without asking.
    /// </summary>
    /// <param name="state">The outcome.</param>
    /// <param name="link">The link.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the outcome is published.</returns>
    private Task PublishAsync(LinkState state, ChainLinkConfig link, CancellationToken cancellationToken)
        => _events.PublishLinkStateAsync(new LinkStateChangedEvent(WithContact(state, link)), cancellationToken);

    /// <summary>
    /// Opens the path to the peer and asks it whether it is ready to exchange with this node.
    /// </summary>
    /// <param name="link">The link to the counterpart endpoint.</param>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; a failure of the ping is an outcome, not an exception.</returns>
    private async Task<LinkState> PingOnceAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken)
    {
        var peer = link.Peer.Id;
        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            return State(link, LinkStatus.Failed, _localizer.Format("Ping.NoToken", peer));
        }

        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(chain);
        }
        catch (ArgumentException ex)
        {
            return State(link, LinkStatus.Failed, _localizer.Format("Ping.NoPath", peer, _localizer.Describe(ex)));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.Timeout);
        var started = _timeProvider.GetUtcNow();
        try
        {
            PeerLink path;
            try
            {
                path = await _links.OpenAsync(route, timeout.Token);
            }
            catch (SyncBridgeException ex)
            {
                return State(link, LinkStatus.Failed, _localizer.Format("Ping.NoPath", peer, _localizer.Describe(ex)));
            }

            await using (path)
            {
                using var request = path.CreateRequest(HttpMethod.Get, ApiRoutes.PeerPing, _node.Id.Value, token);
                HttpResponseMessage response;
                try
                {
                    response = await path.Client.SendAsync(request, timeout.Token);
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException)
                {
                    return State(link, LinkStatus.Failed, _localizer.Format("Ping.NoAnswer", peer, ex.Message));
                }

                using (response)
                {
                    var text = await response.Content.ReadAsStringAsync(timeout.Token);
                    var elapsed = (long)(_timeProvider.GetUtcNow() - started).TotalMilliseconds;
                    return Read(link, chain, response.StatusCode, text, elapsed);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return State(link, LinkStatus.Failed, _localizer.Format("Ping.Timeout", peer, (int)_options.Timeout.TotalSeconds));
        }
    }

    /// <summary>
    /// Reads the answer of the peer.
    /// </summary>
    /// <param name="link">The link to the counterpart endpoint.</param>
    /// <param name="chain">All links of the node.</param>
    /// <param name="status">The status code of the answer.</param>
    /// <param name="text">The body of the answer.</param>
    /// <param name="elapsedMilliseconds">How long the question and the answer took.</param>
    /// <returns>The outcome.</returns>
    private LinkState Read(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, HttpStatusCode status, string text, long elapsedMilliseconds)
    {
        var peer = link.Peer.Id;
        if (status == HttpStatusCode.OK && TryParse<PeerPingResponse>(text) is { NodeId: not null } pong)
        {
            if (!string.Equals(pong.NodeId, peer.Value, StringComparison.Ordinal))
            {
                return State(link, LinkStatus.Failed, _localizer.Format("Ping.WrongNode", peer, pong.NodeId));
            }

            var at = _timeProvider.GetUtcNow().ToLocalTime().ToString("T", _localizer.Culture);
            var mediators = chain.Where(candidate => candidate.Id != link.Id)
                .OrderBy(candidate => candidate.Priority)
                .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
                .Select(candidate => candidate.Peer.Id.Value)
                .ToArray();
            return State(link, LinkStatus.Ready, mediators.Length == 0
                ? _localizer.Format("Ping.ReadyDirect", peer, elapsedMilliseconds, at)
                : _localizer.Format("Ping.Ready", peer, string.Join(", ", mediators), elapsedMilliseconds, at));
        }

        // A refusal of the peer names its reason; a version of the peer without the ping answers the unknown route the
        // way it answers every route that is not for neighbours.
        if (RefusalOf(text) is { } refusal)
        {
            return State(link, LinkStatus.Failed, _localizer.Format("Ping.Refused", peer, refusal));
        }

        return status is HttpStatusCode.Unauthorized or HttpStatusCode.NotFound
            ? State(link, LinkStatus.Degraded, _localizer.Format("Ping.OldVersion", peer))
            : State(link, LinkStatus.Failed, _localizer.Format("Ping.BadAnswer", peer, (int)status));
    }

    /// <summary>
    /// Creates the state of the link.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="status">The status.</param>
    /// <param name="message">The message.</param>
    /// <returns>The state, checked now.</returns>
    private LinkState State(ChainLinkConfig link, LinkStatus status, string message)
        => new(link.Id, link.Name, link.Transport, status, message, _timeProvider.GetUtcNow());

    /// <summary>
    /// Adds to a state when the peer last reached this node, the other half of the exchange.
    /// </summary>
    /// <param name="state">The state of the link.</param>
    /// <param name="link">The link.</param>
    /// <returns>The state with the contact of the peer, or the state itself when the peer has not reached this node.</returns>
    private LinkState WithContact(LinkState state, ChainLinkConfig link)
    {
        if (_contacts.LastHeard(link.Peer.Id) is not { } heard)
        {
            return state;
        }

        var incoming = _localizer.Format("Ping.Incoming", heard.ToLocalTime().ToString("T", _localizer.Culture));
        return new LinkState(state.LinkId, state.LinkName, state.Kind, state.Status, $"{state.Message} {incoming}", state.UpdatedAtUtc);
    }

    /// <summary>
    /// Reads the reason of a refusal: an answer that names a reason and words it.
    /// </summary>
    /// <param name="text">The body of the answer.</param>
    /// <returns>The wording of the refusal, or <see langword="null"/> when the answer is not a refusal of the peer.</returns>
    private static string? RefusalOf(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("reason", out _)
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.String
                ? error.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a JSON answer.
    /// </summary>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <param name="text">The body.</param>
    /// <returns>The value, or <see langword="null"/> when the body is not such a value.</returns>
    private static T? TryParse<T>(string text)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
