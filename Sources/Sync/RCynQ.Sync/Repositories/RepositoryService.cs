using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Reachability;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Repositories;

/// <summary>
/// The operations on the repositories of the node, as the local API and the command line offer them.
/// </summary>
/// <remarks>
/// A Workstation may add only a repository the Origin offers: it names it, and the Origin decides which folder that
/// is. No request ever carries a path of the other computer.
/// </remarks>
public sealed class RepositoryService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The repositories of the node.</summary>
    private readonly RepositoryCatalog _catalog;

    /// <summary>What is known about the repository of the other node.</summary>
    private readonly PeerRepositoryTracker _tracker;

    /// <summary>The links of the node.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>Asks the Origin for what it offers.</summary>
    private readonly PeerRepositoryClient _client;

    /// <summary>Checks that a request comes from the counterpart endpoint.</summary>
    private readonly PatchAdmission _admission;

    /// <summary>Pings the counterpart endpoint, or <see langword="null"/> when the host has none.</summary>
    private readonly EndpointPinger? _pinger;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="catalog">The repositories of the node.</param>
    /// <param name="tracker">What is known about the repository of the other node.</param>
    /// <param name="links">The links of the node.</param>
    /// <param name="client">Asks the Origin for what it offers.</param>
    /// <param name="admission">Checks that a request comes from the counterpart endpoint.</param>
    /// <param name="pinger">Pings the counterpart endpoint so it learns of a switch at once; <see langword="null"/> leaves that to its next ping.</param>
    public RepositoryService(
        ILocalizer localizer,
        NodeConfig node,
        RepositoryCatalog catalog,
        PeerRepositoryTracker tracker,
        IChainLinkRepository links,
        PeerRepositoryClient client,
        PatchAdmission admission,
        EndpointPinger? pinger = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _pinger = pinger;
    }

    /// <summary>
    /// Lists the repositories of the node. A Workstation also lists what the Origin offers and it does not keep yet.
    /// </summary>
    /// <param name="askOrigin">Whether a Workstation asks the Origin for what it offers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The repositories; when the Origin cannot be asked, the local ones and the reason.</returns>
    public async Task<RepositoryListDto> ListAsync(bool askOrigin = true, CancellationToken cancellationToken = default)
    {
        var current = _catalog.Current;
        IReadOnlyList<string>? offered = null;
        string? error = null;
        if (_node.Role == NodeRole.Workstation && askOrigin)
        {
            try
            {
                var answer = await _client.GetAsync(await _links.GetAllAsync(cancellationToken), cancellationToken);
                offered = answer.Names;
                await _tracker.RecordAsync(answer.Active, cancellationToken);
                current = _catalog.Current;
            }
            catch (SyncBridgeException ex)
            {
                error = _localizer.Describe(ex);
            }
        }

        var repositories = _catalog.Entries
            .Select(entry => new RepositoryDto(
                entry.Name,
                entry.Path,
                entry.Id == current?.Id,
                true,
                _node.Role == NodeRole.Origin ? true : offered?.Contains(entry.Name, StringComparer.OrdinalIgnoreCase)))
            .ToList();
        if (offered is not null)
        {
            repositories.AddRange(offered
                .Where(name => RepositoryName.IsValid(name) && _catalog.Find(name) is null)
                .Select(name => new RepositoryDto(name, null, false, false, true)));
        }

        return new RepositoryListDto(
            _node.Role,
            current is { IsBound: true } ? current.Name : null,
            _tracker.PeerRepository,
            repositories,
            error);
    }

    /// <summary>
    /// Adds a repository to the node.
    /// </summary>
    /// <param name="name">The name the nodes call the repository by.</param>
    /// <param name="path">The local path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The new entry.</returns>
    /// <exception cref="SyncBridgeException">The repository cannot be added; the message tells why.</exception>
    public async Task<RepositoryEntry> AddAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        if (_node.Role == NodeRole.Workstation)
        {
            var answer = await _client.GetAsync(await _links.GetAllAsync(cancellationToken), cancellationToken);
            if (!answer.Names.Contains((name ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                throw LocalizedError.Create(message => new SyncBridgeException(message), "Repository.NotOffered", name);
            }
        }

        return await _catalog.AddAsync(name!, path, cancellationToken);
    }

    /// <summary>
    /// Removes a repository from the node. Its files stay where they are.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the repository is removed.</returns>
    public Task RemoveAsync(string name, CancellationToken cancellationToken = default)
        => _catalog.RemoveAsync(name, cancellationToken);

    /// <summary>
    /// Makes a repository the one the node works with and lets the other node know.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The repository the node works with now.</returns>
    public async Task<RepositoryEntry> ActivateAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry = await _catalog.ActivateAsync(name, cancellationToken);
        _ = NotifyPeerAsync();
        return entry;
    }

    /// <summary>
    /// Answers a Workstation that asks which repositories this Origin offers.
    /// </summary>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The names of the repositories.</returns>
    /// <exception cref="PatchRejectedException">The request is not allowed.</exception>
    public async Task<PeerRepositoriesResponse> AnswerPeerAsync(PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _admission.AuthorizePeerAsync(context, cancellationToken);
        if (_node.Role != NodeRole.Origin)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, _localizer.Format("Repository.OnlyOriginOffers", _node.Role));
        }

        return new PeerRepositoriesResponse(
            _catalog.Entries.Where(entry => entry.IsBound).Select(entry => entry.Name).ToArray(),
            _catalog.Current is { IsBound: true } current ? current.Name : null);
    }

    /// <summary>
    /// Pings the other node, which carries the name of the repository this node works with. A failure is not reported:
    /// the other node learns of the switch with its own next ping.
    /// </summary>
    /// <returns>A task that completes when the ping was tried.</returns>
    private async Task NotifyPeerAsync()
    {
        if (_pinger is null)
        {
            return;
        }

        try
        {
            await _pinger.PingAsync(await _links.GetAllAsync());
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or InvalidOperationException or OperationCanceledException or ObjectDisposedException)
        {
            // Best effort by design.
        }
    }
}
