using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;

namespace RCynQ.Sync.Configuration;

/// <summary>
/// Saves chain links that are described by hand: in the configuration file, in the application or on the command line.
/// </summary>
/// <remarks>
/// <para>
/// Such a description names the address of the peer but not everything the node knows about it: the SSH public key, the
/// endpoint mode and the TLS fingerprint of a peer come only with an import. Saving the link through the repository alone
/// would replace the stored peer with the shorter description and silently erase them, on every start of a node whose
/// links are listed in the configuration file. Every such save therefore goes through this class, which completes the
/// description with what is stored (see <see cref="PeerDescriptor.WithDetailsFrom"/>).
/// </para>
/// <para>
/// An import does not use this class: it states every value of the peer itself and is the one way to change them.
/// </para>
/// </remarks>
public sealed class ConfiguredLinkStore
{
    /// <summary>The chain link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredLinkStore"/> class.
    /// </summary>
    /// <param name="links">The chain link repository.</param>
    /// <param name="peers">The peer repository.</param>
    public ConfiguredLinkStore(IChainLinkRepository links, IPeerRepository peers)
    {
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _peers = peers ?? throw new ArgumentNullException(nameof(peers));
    }

    /// <summary>
    /// Validates and saves a link described by options.
    /// </summary>
    /// <param name="options">The link as it was written or typed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The saved link, with the completed peer.</returns>
    /// <exception cref="ArgumentException">The options do not describe a valid link.</exception>
    public Task<ChainLinkConfig> SaveAsync(ChainLinkOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        return SaveAsync(SyncBridgeOptionsValidator.ToChainLink(options), cancellationToken);
    }

    /// <summary>
    /// Saves a link, keeping what is already stored about its peer and the link does not state.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The saved link, with the completed peer.</returns>
    public async Task<ChainLinkConfig> SaveAsync(ChainLinkConfig link, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(link);
        var stored = await _peers.GetAsync(link.Peer.Id, cancellationToken);
        var peer = link.Peer.WithDetailsFrom(stored);
        var completed = ReferenceEquals(peer, link.Peer) ? link : link.WithPeer(peer);
        var known = await _links.GetAsync(link.Id, cancellationToken);
        return await _links.UpsertAsync(known is null ? completed : WithKnownRoutes(completed, known), cancellationToken);
    }

    /// <summary>
    /// Keeps what the node knows about the routes of a link and a hand-written description does not state.
    /// </summary>
    /// <remarks>
    /// Routes come with imports and the route in use is chosen while the node runs. A description written by hand names
    /// one address, and it is saved again on every start of a node whose links are listed in the configuration file:
    /// without this the routes would be erased and the node would return to the written address each time.
    /// </remarks>
    /// <param name="link">The link as it was written, with the completed peer.</param>
    /// <param name="known">The stored link with the same identifier.</param>
    /// <returns>The link to store.</returns>
    private static ChainLinkConfig WithKnownRoutes(ChainLinkConfig link, ChainLinkConfig known)
    {
        if (known.Peer.Id != link.Peer.Id)
        {
            // The link leads to another node now; routes to the old one mean nothing.
            return link;
        }

        if (link.Routes.Count > 0)
        {
            // The description lists the routes itself. The choice made while running is kept where it still applies.
            var pinned = known.PinnedRouteId is { } id && link.Routes.Any(route => route.Id == id) ? known.PinnedRouteId : null;
            var inUse = link.Routes.Any(route => route.LeadsTo(known.Peer.Ssh)) ? link.WithPeer(link.Peer.WithAddress(known.Peer.Ssh)) : link;
            return inUse.WithRoutes(link.Routes, pinned);
        }

        if (known.Routes.Count == 0)
        {
            return link;
        }

        if (known.Routes.Any(route => route.LeadsTo(link.Peer.Ssh)))
        {
            // The written address is one of the routes: the route in use stays the one the node chose.
            return link.WithPeer(link.Peer.WithAddress(known.Peer.Ssh)).WithRoutes(known.Routes, known.PinnedRouteId);
        }

        // The written address is new: it is the route in use with its address corrected.
        var active = known.ActiveRoute ?? known.Routes[0];
        var routes = known.Routes.Select(route => route.Id == active.Id ? route.WithAddress(link.Peer.Ssh) : route).ToArray();
        return link.WithRoutes(routes, known.PinnedRouteId);
    }

    /// <summary>
    /// Saves every link of the <c>Chain</c> section of the configuration.
    /// </summary>
    /// <param name="options">The validated configuration of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number of saved links.</returns>
    public async Task<int> SeedAsync(SyncBridgeOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        var links = SyncBridgeOptionsValidator.ToChainLinks(options);
        foreach (var link in links)
        {
            await SaveAsync(link, cancellationToken);
        }

        return links.Count;
    }
}
