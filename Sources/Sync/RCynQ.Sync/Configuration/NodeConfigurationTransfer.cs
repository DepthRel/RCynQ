using System.Security.Cryptography;
using System.Text.Json;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Localization;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Endpoint;

namespace RCynQ.Sync.Configuration;

/// <summary>
/// Exports the public configuration of the local node and imports the configuration of peers.
/// </summary>
/// <remarks>
/// Import is the explicit trust decision of the pairing: the peer's signing key becomes accepted for patch verification.
/// When the peer is new and reachable, a chain link is created in the right position: relays before the endpoint.
/// </remarks>
public sealed class NodeConfigurationTransfer
{
    /// <summary>
    /// The priority assigned to endpoint links so relays come first.
    /// </summary>
    public const int ENDPOINT_PRIORITY = 1000;

    /// <summary>
    /// JSON options of the export file.
    /// </summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeConfigurationTransfer"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="options">The node options.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="tlsFingerprint">
    /// Returns the fingerprint of the node's TLS certificate for the export file, or <see langword="null"/> when the node uses its
    /// SSH server; <see langword="null"/> when no certificate is published.
    /// </param>
    public NodeConfigurationTransfer(
        ILocalizer localizer,
        NodeConfig node,
        SyncBridgeOptions options,
        ISignatureService signatures,
        IPeerRepository peers,
        IChainLinkRepository links,
        TimeProvider timeProvider,
        Func<string?>? tlsFingerprint = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _options = options;
        _signatures = signatures;
        _peers = peers;
        _links = links;
        _timeProvider = timeProvider;
        _tlsFingerprint = tlsFingerprint ?? (() => null);
    }

    /// <summary>
    /// Reads the fingerprint of this node's TLS certificate, or <see langword="null"/> when it does not exist.
    /// </summary>
    private readonly Func<string?> _tlsFingerprint;

    /// <summary>
    /// Exports the public configuration of the local node.
    /// </summary>
    /// <param name="addresses">
    /// The addresses at which neighbours reach this node, the preferred one first; empty when the importing neighbour
    /// knows the address already.
    /// </param>
    /// <param name="sshUser">The SSH user neighbours log in with, or <see langword="null"/>.</param>
    /// <param name="sshPort">The SSH port.</param>
    /// <param name="sshPublicKey">This node's SSH public key line, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The SHA-256 fingerprint of this node's sshd host key, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The exported configuration.</returns>
    /// <exception cref="ArgumentException">A value is invalid.</exception>
    public async Task<ExportedNodeConfiguration> ExportAsync(IReadOnlyList<string> addresses, string? sshUser, int sshPort = 22, string? sshPublicKey = null, string? hostKeyFingerprint = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var normalizedFingerprint = NormalizeHostKeyFingerprint(hostKeyFingerprint)
            ?? (string.IsNullOrWhiteSpace(hostKeyFingerprint) ? null : throw new ArgumentException(_localizer.Format("Transfer.HostKeyFingerprintValue", hostKeyFingerprint), nameof(hostKeyFingerprint)));

        var hosts = addresses.Select(address => address.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var invalid = hosts.FirstOrDefault(host => !SshEndpoint.IsValidHost(host));
        if (invalid is not null)
        {
            throw new ArgumentException(_localizer.Format("Transfer.HostInvalid", invalid), nameof(addresses));
        }

        if (hosts.Length > ExportedNodeConfiguration.MAX_ADDRESSES)
        {
            throw new ArgumentException(_localizer.Format("Transfer.TooManyAddresses", hosts.Length, ExportedNodeConfiguration.MAX_ADDRESSES), nameof(addresses));
        }

        if (sshUser is not null && !SshEndpoint.IsValidUser(sshUser))
        {
            throw new ArgumentException(_localizer.Format("Transfer.UserInvalid", sshUser), nameof(sshUser));
        }

        Guard.Port(sshPort, nameof(sshPort));
        string? normalizedSshKey = null;
        if (!string.IsNullOrWhiteSpace(sshPublicKey))
        {
            try
            {
                normalizedSshKey = SshPublicKey.Parse(sshPublicKey).ToLine();
            }
            catch (FormatException ex)
            {
                throw new ArgumentException(_localizer.Describe(ex), nameof(sshPublicKey), ex);
            }
        }

        var signingKey = _node.ParticipatesInPatchChannel
            ? Convert.ToBase64String(await _signatures.ExportPublicKeyAsync(cancellationToken))
            : null;

        return new ExportedNodeConfiguration(
            ExportedNodeConfiguration.CURRENT_FORMAT_VERSION,
            _node.Id,
            _node.Name,
            _node.Role,
            signingKey,
            hosts.FirstOrDefault(),
            sshUser,
            sshPort,
            _options.ListenPort,
            normalizedSshKey,
            _timeProvider.GetUtcNow(),
            normalizedFingerprint,
            _options.Endpoint.Mode,
            _options.Endpoint.Mode == EndpointMode.Direct ? _options.Endpoint.Port : 0,
            _options.Endpoint.Mode == EndpointMode.Direct ? EndpointCertificateFingerprint() : null,
            hosts.Length == 0 ? null : hosts,
            ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION);
    }

    /// <summary>
    /// Gets the fingerprint of the node's TLS certificate for the export.
    /// </summary>
    /// <returns>The fingerprint.</returns>
    /// <exception cref="SyncBridgeException">The certificate does not exist yet.</exception>
    private string EndpointCertificateFingerprint()
        => _tlsFingerprint()
            ?? throw new SyncBridgeException(
                _localizer.Get("Transfer.NoCertificate"));

    /// <summary>
    /// Imports the configuration of a peer.
    /// </summary>
    /// <param name="imported">The peer configuration.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result.</returns>
    /// <exception cref="FormatException">The configuration is invalid or incomplete.</exception>
    public Task<ImportResult> ImportAsync(ExportedNodeConfiguration imported, CancellationToken cancellationToken = default)
        => ImportAsync(imported, null, null, cancellationToken);

    /// <summary>
    /// Imports the configuration of a peer, with the decision an earlier attempt asked for.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An import that meets no peer of the imported role, or that repeats an address the node already knows, needs no
    /// decision and behaves as it always did. Otherwise nothing is stored without one: the result names the decisions
    /// that make sense (see <see cref="ImportResult.Decision"/>) and the import is repeated with one of them.
    /// </para>
    /// <para>
    /// A node known at another address either gets one more route (<see cref="ImportDecision.AlternativeRoute"/>) or its
    /// address is replaced (<see cref="ImportDecision.Replace"/>). Another node of a role that is already linked either
    /// takes the place of one of the links (<see cref="ImportDecision.Replace"/>; the replaced node is forgotten with its
    /// keys) or, when it is a Relay or this node is one, is added next to them (<see cref="ImportDecision.AddHop"/>).
    /// </para>
    /// </remarks>
    /// <param name="imported">The peer configuration.</param>
    /// <param name="decision">The decision, or <see langword="null"/> when none was made.</param>
    /// <param name="targetLinkId">
    /// The link whose peer is replaced by another node; may be <see langword="null"/> when only one link qualifies.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result, or a request for a decision.</returns>
    /// <exception cref="FormatException">
    /// The configuration is invalid or incomplete, or the decision does not fit the import.
    /// </exception>
    public async Task<ImportResult> ImportAsync(ExportedNodeConfiguration imported, ImportDecision? decision, Guid? targetLinkId, CancellationToken cancellationToken = default)
    {
        Validate(imported);
        if (imported.NodeId == _node.Id)
        {
            throw new FormatException(_localizer.Get("Transfer.ThisNode"));
        }

        var existing = await _peers.GetAsync(imported.NodeId, cancellationToken);
        var addresses = imported.ReachableAt();
        var host = addresses.FirstOrDefault() ?? existing?.Host
            ?? throw new FormatException(_localizer.Format("Transfer.NoHost", imported.NodeId));
        var user = imported.SshUser ?? existing?.SshUser
            ?? throw new FormatException(_localizer.Format("Transfer.NoUser", imported.NodeId));
        var port = imported.SshPort > 0 ? imported.SshPort : existing?.SshPort ?? 22;

        PeerDescriptor peer;
        try
        {
            peer = new PeerDescriptor(
                imported.NodeId, imported.Name, imported.Role, host, port, user,
                NormalizeHostKeyFingerprint(imported.HostKeyFingerprint) ?? existing?.HostKeyFingerprint,
                imported.SigningPublicKey ?? existing?.SigningPublicKey,
                imported.SshPublicKey is null ? existing?.SshPublicKey : SshPublicKey.Parse(imported.SshPublicKey).ToLine(),
                imported.EndpointMode,
                imported.TlsFingerprint is null
                    ? imported.EndpointMode == existing?.EndpointMode ? existing.TlsFingerprint : null
                    : NodeCertificateStore.NormalizeFingerprint(imported.TlsFingerprint),
                imported.EndpointMode == EndpointMode.Direct ? imported.EndpointPort : 0);
        }
        catch (ArgumentException ex)
        {
            throw new FormatException(_localizer.Format("Transfer.Invalid", imported.NodeId, _localizer.Describe(ex)), ex);
        }

        var counterpart = _node.Role switch
        {
            NodeRole.Workstation => NodeRole.Origin,
            NodeRole.Origin => NodeRole.Workstation,
            _ => (NodeRole?)null
        };

        // The port of the counterpart endpoint a link leads to: its TLS port when it runs no SSH server, otherwise its
        // loopback endpoint behind the SSH tunnel.
        var endpointPort = peer.EndpointMode == EndpointMode.Direct ? imported.EndpointPort : imported.ListenPort;

        var links = await _links.GetAllAsync(cancellationToken);
        var linked = links.Where(link => link.Peer.Id == peer.Id).ToList();

        // A new export of the counterpart may name another port (it changed its endpoint mode or its listen port);
        // a link that kept the old one would lead nowhere. The port of a link to a relay is not part of an export.
        int? linkedPort = peer.Role == counterpart ? endpointPort : null;
        // A card that lists its addresses describes the node completely; one of 0.1.0 names a single address, which may
        // be one more route or a new one, and only the user knows which. A decision the user made wins either way.
        var listsAddresses = imported.Addresses is { Count: > 0 };
        if (linked.Count > 0)
        {
            return listsAddresses && decision is null
                ? await RefreshKnownNodeAsync(peer, linked, addresses, linkedPort, cancellationToken)
                : await ImportKnownNodeAsync(peer, linked, linkedPort, decision, cancellationToken);
        }

        var joinsAsNeighbour = peer.Role == NodeRole.Relay || _node.Role == NodeRole.Relay;
        if (!joinsAsNeighbour && peer.Role != counterpart)
        {
            await _peers.UpsertAsync(peer, cancellationToken);
            return new ImportResult(peer.Id, peer.Role, false, null, _localizer.Format("Transfer.StoredOnly", peer.Id, _node.Role, peer.Role));
        }

        var sameRole = links.Where(link => link.Peer.Role == peer.Role).ToList();
        if (sameRole.Count > 0)
        {
            // Two counterpart endpoints cannot share a chain: the last link is the one patches go to.
            var options = joinsAsNeighbour
                ? new[] { ImportDecision.AddHop, ImportDecision.Replace }
                : new[] { ImportDecision.Replace };
            if (decision is null)
            {
                return NeedsDecision(peer, false, options, sameRole);
            }

            if (!options.Contains(decision.Value))
            {
                throw new FormatException(_localizer.Format("Transfer.DecisionNotAllowed", decision.Value, peer.Id));
            }

            if (decision == ImportDecision.Replace)
            {
                return await ReplaceNodeAsync(peer, sameRole, targetLinkId, joinsAsNeighbour ? null : endpointPort, RoutesTo(peer, addresses), cancellationToken);
            }
        }

        var priority = joinsAsNeighbour
            ? links.Where(link => link.Priority < ENDPOINT_PRIORITY).Select(link => link.Priority + 10).DefaultIfEmpty(10).Max()
            : links.Select(link => link.Priority + 10).Append(ENDPOINT_PRIORITY).Max();
        var name = UniqueName(peer.Id.Value, links.Select(link => link.Name));
        var created = new ChainLinkConfig(Guid.NewGuid(), name, TransportKind.Direct, peer, 0, joinsAsNeighbour ? 22 : endpointPort, priority, routes: RoutesTo(peer, addresses));
        await _peers.UpsertAsync(peer, cancellationToken);
        await _links.UpsertAsync(created, cancellationToken);
        return new ImportResult(peer.Id, peer.Role, true, created.Id, _localizer.Format("Transfer.Imported", peer.Role, peer.Id, name));
    }

    /// <summary>
    /// Imports a new export of a node the chain already leads to.
    /// </summary>
    /// <param name="peer">The imported description of the node.</param>
    /// <param name="linked">The links that lead to the node.</param>
    /// <param name="remotePort">The remote port the links get, or <see langword="null"/> to keep theirs.</param>
    /// <param name="decision">The decision, or <see langword="null"/> when none was made.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result, or a request for a decision.</returns>
    /// <exception cref="FormatException">The decision does not fit the import.</exception>
    private async Task<ImportResult> ImportKnownNodeAsync(PeerDescriptor peer, IReadOnlyList<ChainLinkConfig> linked, int? remotePort, ImportDecision? decision, CancellationToken cancellationToken)
    {
        var address = peer.Ssh;
        var isKnownAddress = linked.Any(link => LinkRoute.SameAddress(link.Peer.Ssh, address) || link.Routes.Any(route => route.LeadsTo(address)));
        if (isKnownAddress)
        {
            // Keys and ports are refreshed; the address in use stays the one the node chose among the routes.
            await _peers.UpsertAsync(peer.WithAddress(linked[0].Peer.Ssh), cancellationToken);
            foreach (var link in linked)
            {
                await _links.UpsertAsync(link.WithPeer(peer.WithAddress(link.Peer.Ssh), remotePort), cancellationToken);
            }

            return new ImportResult(peer.Id, peer.Role, false, linked[0].Id, _localizer.Format("Transfer.Updated", peer.Id, linked.Count));
        }

        var options = new[] { ImportDecision.AlternativeRoute, ImportDecision.Replace };
        if (decision is null)
        {
            return NeedsDecision(peer, true, options, linked);
        }

        if (!options.Contains(decision.Value))
        {
            throw new FormatException(_localizer.Format("Transfer.DecisionNotAllowed", decision.Value, peer.Id));
        }

        await _peers.UpsertAsync(
            decision == ImportDecision.AlternativeRoute ? peer.WithAddress(linked[0].Peer.Ssh) : peer,
            cancellationToken);
        foreach (var link in linked)
        {
            if (decision == ImportDecision.AlternativeRoute)
            {
                // The address the link had so far becomes a route of its own the first time another one is added.
                var routes = link.Routes.Count > 0
                    ? link.Routes.ToList()
                    : new List<LinkRoute> { new(Guid.NewGuid(), link.Peer.Host, link.Peer.Ssh) };
                routes.Add(new LinkRoute(Guid.NewGuid(), UniqueName(address.Host, routes.Select(route => route.Name)), address));
                await _links.UpsertAsync(
                    link.WithPeer(peer.WithAddress(link.Peer.Ssh), remotePort).WithRoutes(routes, link.PinnedRouteId),
                    cancellationToken);
            }
            else
            {
                // The route in use moves to the new address; the other routes are kept.
                var active = link.ActiveRoute;
                var routes = link.Routes.Select(route => route.Id == active?.Id ? route.WithAddress(address) : route).ToArray();
                await _links.UpsertAsync(link.WithRoutes(routes, link.PinnedRouteId).WithPeer(peer, remotePort), cancellationToken);
            }
        }

        return new ImportResult(
            peer.Id, peer.Role, false, linked[0].Id,
            decision == ImportDecision.AlternativeRoute
                ? _localizer.Format("Transfer.RouteAdded", address.Host, peer.Id)
                : _localizer.Format("Transfer.Updated", peer.Id, linked.Count));
    }

    /// <summary>
    /// Imports a card that lists every address of a node the chain already leads to: its addresses become the routes of
    /// each link to it. A pinned route whose address is still listed stays pinned, the address in use stays in use when it
    /// is still listed, otherwise the link moves to the first address of the card.
    /// </summary>
    /// <param name="peer">The imported description of the node.</param>
    /// <param name="linked">The links that lead to the node.</param>
    /// <param name="addresses">The addresses of the card, the preferred one first.</param>
    /// <param name="remotePort">The remote port the links get, or <see langword="null"/> to keep theirs.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result.</returns>
    private async Task<ImportResult> RefreshKnownNodeAsync(PeerDescriptor peer, IReadOnlyList<ChainLinkConfig> linked, IReadOnlyList<string> addresses, int? remotePort, CancellationToken cancellationToken)
    {
        SshEndpoint? inUse = null;
        foreach (var link in linked)
        {
            // A route that leads to a listed address keeps its identifier, so a pinned route stays pinned.
            var routes = RoutesTo(peer, addresses)?
                .Select(route => link.Routes.FirstOrDefault(known => known.LeadsTo(route.Ssh)) is { } known
                    ? new LinkRoute(known.Id, known.Name, route.Ssh, route.Priority)
                    : route)
                .ToArray();
            var pinned = routes?.FirstOrDefault(route => route.Id == link.PinnedRouteId);
            var address = pinned?.Ssh
                ?? routes?.FirstOrDefault(route => route.LeadsTo(link.Peer.Ssh))?.Ssh
                ?? peer.Ssh;

            await _links.UpsertAsync(link.WithRoutes(routes, pinned?.Id).WithPeer(peer.WithAddress(address), remotePort), cancellationToken);
            inUse ??= address;
        }

        await _peers.UpsertAsync(peer.WithAddress(inUse ?? peer.Ssh), cancellationToken);
        return new ImportResult(peer.Id, peer.Role, false, linked[0].Id, _localizer.Format("Transfer.Updated", peer.Id, linked.Count));
    }

    /// <summary>
    /// Builds the routes of a link from the addresses of a card: one route per address, in the order of the card.
    /// </summary>
    /// <param name="peer">The imported node; its SSH user and port are those of every route.</param>
    /// <param name="addresses">The addresses, the preferred one first; a repeated one counts once.</param>
    /// <returns>The routes, or <see langword="null"/> for a single address: such a link has no routes.</returns>
    private static LinkRoute[]? RoutesTo(PeerDescriptor peer, IReadOnlyList<string> addresses)
    {
        const int PRIORITY_STEP = 10;

        // A card written by hand may repeat an address; one route per address is enough.
        var distinct = addresses.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (distinct.Length < 2)
        {
            return null;
        }

        return distinct
            .Select((address, index) => new LinkRoute(Guid.NewGuid(), address, new SshEndpoint(peer.SshUser, address, peer.SshPort), index * PRIORITY_STEP))
            .ToArray();
    }

    /// <summary>
    /// Puts an imported node in the place of the peer of a link and forgets the replaced peer.
    /// </summary>
    /// <param name="peer">The imported node.</param>
    /// <param name="candidates">The links to peers of the same role.</param>
    /// <param name="targetLinkId">The link to change, or <see langword="null"/> when only one qualifies.</param>
    /// <param name="remotePort">The remote port the link gets, or <see langword="null"/> to keep its own.</param>
    /// <param name="routes">The routes to the imported node, or <see langword="null"/> when it has one address.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The import result.</returns>
    /// <exception cref="FormatException">The link is not named although several qualify, or it does not qualify.</exception>
    private async Task<ImportResult> ReplaceNodeAsync(PeerDescriptor peer, IReadOnlyList<ChainLinkConfig> candidates, Guid? targetLinkId, int? remotePort, IReadOnlyList<LinkRoute>? routes, CancellationToken cancellationToken)
    {
        var target = targetLinkId is { } id
            ? candidates.FirstOrDefault(link => link.Id == id)
                ?? throw new FormatException(_localizer.Format("Transfer.ReplaceTargetUnknown", id, peer.Role))
            : candidates.Count == 1
                ? candidates[0]
                : throw new FormatException(_localizer.Format("Transfer.ReplaceTargetNeeded", peer.Role));

        var replaced = target.Peer;

        // The routes led to the replaced node; the new one starts with the addresses of its export.
        await _peers.UpsertAsync(peer, cancellationToken);
        await _links.UpsertAsync(target.WithRoutes(routes).WithPeer(peer, remotePort), cancellationToken);

        // Otherwise the SSH key of the replaced node would keep its access and its signing key would stay trusted.
        await _peers.DeleteUnlinkedAsync(replaced.Id, cancellationToken);
        return new ImportResult(peer.Id, peer.Role, false, target.Id, _localizer.Format("Transfer.Replaced", replaced.Id, peer.Id, target.Name));
    }

    /// <summary>
    /// Builds the answer of an import that waits for a decision; nothing was stored.
    /// </summary>
    /// <param name="peer">The imported node.</param>
    /// <param name="sameNode">Whether the node is already known at another address.</param>
    /// <param name="options">The decisions that make sense.</param>
    /// <param name="candidates">The links the decision is about.</param>
    /// <returns>The result.</returns>
    private ImportResult NeedsDecision(PeerDescriptor peer, bool sameNode, IReadOnlyList<ImportDecision> options, IReadOnlyList<ChainLinkConfig> candidates)
        => new(peer.Id, peer.Role, false, null, _localizer.Format(sameNode ? "Transfer.NeedsDecisionSameNode" : "Transfer.NeedsDecisionOtherNode", peer.Id, peer.Role, peer.Host))
        {
            Decision = new ImportDecisionRequest(
                sameNode,
                peer.Host,
                options,
                candidates.Select(link => new ImportCandidate(link.Id, link.Name, link.Peer.Id, link.Peer.Name, link.Peer.Role, link.Peer.Host)).ToArray())
        };

    /// <summary>
    /// Serializes an exported configuration to indented JSON.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The JSON text.</returns>
    public static string Serialize(ExportedNodeConfiguration configuration) => JsonSerializer.Serialize(configuration, _jsonOptions);

    /// <summary>
    /// Parses and validates an exported configuration.
    /// </summary>
    /// <param name="json">The JSON text.</param>
    /// <returns>The configuration.</returns>
    /// <exception cref="FormatException">The text is not a valid export.</exception>
    public static ExportedNodeConfiguration Parse(string json)
    {
        ExportedNodeConfiguration? configuration;
        try
        {
            configuration = JsonSerializer.Deserialize<ExportedNodeConfiguration>(json, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw LocalizedError.Create(message => new FormatException(message, ex), "Transfer.FileInvalid", ex);
        }

        Validate(configuration);
        return configuration!;
    }

    /// <summary>
    /// Validates an exported configuration: the checks of an import, also for the cards inside an invitation.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <exception cref="FormatException">The configuration is invalid.</exception>
    public static void Validate(ExportedNodeConfiguration? configuration)
    {
        if (configuration is null)
        {
            throw LocalizedError.Format("Transfer.FileEmpty");
        }

        if (configuration.FormatVersion != ExportedNodeConfiguration.CURRENT_FORMAT_VERSION)
        {
            throw LocalizedError.Format("Transfer.Version", configuration.FormatVersion);
        }

        if (configuration.NodeId.IsEmpty || string.IsNullOrWhiteSpace(configuration.Name) || !Enum.IsDefined(configuration.Role))
        {
            throw LocalizedError.Format("Transfer.MissingFields");
        }

        if (configuration.Addresses is { } addresses)
        {
            if (addresses.Count > ExportedNodeConfiguration.MAX_ADDRESSES)
            {
                throw LocalizedError.Format("Transfer.TooManyAddresses", addresses.Count, ExportedNodeConfiguration.MAX_ADDRESSES);
            }

            if (addresses.Any(address => !SshEndpoint.IsValidHost(address)))
            {
                throw LocalizedError.Format("Transfer.HostInvalid", addresses.First(address => !SshEndpoint.IsValidHost(address)) ?? string.Empty);
            }
        }

        if (configuration.ListenPort is < 1 or > 65535)
        {
            throw LocalizedError.Format("Transfer.ListenPort", configuration.ListenPort);
        }

        if (!Enum.IsDefined(configuration.EndpointMode))
        {
            throw LocalizedError.Format("Transfer.EndpointMode", configuration.EndpointMode);
        }

        if (configuration.EndpointMode == EndpointMode.Direct)
        {
            if (configuration.EndpointPort is < 1 or > 65535)
            {
                throw LocalizedError.Format("Transfer.EndpointPort", configuration.EndpointPort);
            }

            if (NodeCertificateStore.NormalizeFingerprint(configuration.TlsFingerprint) is null)
            {
                throw LocalizedError.Format("Transfer.NeedsFingerprint");
            }
        }

        if (configuration.SigningPublicKey is not null)
        {
            bool isP256;
            try
            {
                using var key = ECDsa.Create();
                key.ImportSubjectPublicKeyInfo(Convert.FromBase64String(configuration.SigningPublicKey), out _);
                isP256 = key.KeySize == 256;
            }
            catch (Exception ex) when (ex is CryptographicException or FormatException)
            {
                throw LocalizedError.Create(message => new FormatException(message, ex), "Transfer.KeyNotValid");
            }

            if (!isP256)
            {
                throw LocalizedError.Format("Transfer.KeyMustBeP256");
            }
        }

        if (configuration.SshPublicKey is not null)
        {
            SshPublicKey.Parse(configuration.SshPublicKey);
        }

        if (!string.IsNullOrWhiteSpace(configuration.HostKeyFingerprint) && NormalizeHostKeyFingerprint(configuration.HostKeyFingerprint) is null)
        {
            throw LocalizedError.Format("Transfer.HostKeyFingerprint");
        }
    }

    /// <summary>
    /// Normalizes an OpenSSH SHA-256 host key fingerprint.
    /// </summary>
    /// <param name="fingerprint">The fingerprint, with or without the <c>SHA256:</c> prefix.</param>
    /// <returns>The normalized fingerprint, or <see langword="null"/> when the value is blank or not a SHA-256 fingerprint.</returns>
    public static string? NormalizeHostKeyFingerprint(string? fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
        {
            return null;
        }

        var normalized = SshPublicKey.NormalizeFingerprint(fingerprint);
        var hash = normalized["SHA256:".Length..];
        return hash.Length == 43 && hash.All(c => char.IsAsciiLetterOrDigit(c) || c is '+' or '/') ? normalized : null;
    }

    /// <summary>
    /// Returns a name that is not used yet.
    /// </summary>
    /// <param name="baseName">The preferred name.</param>
    /// <param name="taken">The names in use.</param>
    /// <returns>The unique name.</returns>
    private static string UniqueName(string baseName, IEnumerable<string> taken)
    {
        var names = taken.ToHashSet(StringComparer.Ordinal);
        var name = baseName;
        for (var i = 2; names.Contains(name); i++)
        {
            name = $"{baseName}-{i}";
        }

        return name;
    }
}
