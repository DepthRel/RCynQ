using System.Globalization;
using RCynQ.Cli.Output;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;

namespace RCynQ.Cli.Services;

/// <summary>
/// Adds, lists and removes chain links in the node database.
/// </summary>
public sealed class LinkCommandService
{
    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The store that saves a typed link without erasing what is known about its peer.</summary>
    private readonly ConfiguredLinkStore _store;

    /// <summary>The localizer of the output.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="store">The store that saves a typed link without erasing what is known about its peer.</param>
    public LinkCommandService(ILocalizer localizer, IChainLinkRepository links, ConfiguredLinkStore store)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = links;
        _store = store;
    }

    /// <summary>
    /// Validates and stores a link.
    /// </summary>
    /// <param name="options">The link settings.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="ArgumentException">The settings are invalid.</exception>
    public async Task<string> AddAsync(ChainLinkOptions options, CancellationToken cancellationToken = default)
    {
        var link = await _store.SaveAsync(options, cancellationToken);
        return _localizer.Format("Cli.Link.Saved", link.Name, link.Id, link.Peer.Role, link.Peer.Id, link.Peer.Ssh.JumpSpec, link.Transport) + "\n";
    }

    /// <summary>
    /// Lists the links in chain order.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The table.</returns>
    public async Task<string> ListAsync(CancellationToken cancellationToken = default)
    {
        var links = await _links.GetAllAsync(cancellationToken);
        if (links.Count == 0)
        {
            return _localizer.Get("Cli.Link.None") + "\n";
        }

        var table = new ConsoleTable(
            _localizer.Get("Cli.Column.Pos"),
            _localizer.Get("Cli.Column.Name"),
            _localizer.Get("Cli.Column.Peer"),
            _localizer.Get("Cli.Column.Role"),
            _localizer.Get("Cli.Column.Address"),
            _localizer.Get("Cli.Column.Transport"),
            _localizer.Get("Cli.Column.Remote"),
            _localizer.Get("Cli.Column.Signed"),
            _localizer.Get("Cli.Column.Id"));
        foreach (var link in links)
        {
            table.AddRow(
                link.Priority.ToString(CultureInfo.InvariantCulture),
                link.Name,
                link.Peer.Id.Value,
                link.Peer.Role.ToString(),
                link.Peer.Ssh.JumpSpec,
                link.FallbackTransports.Count == 0 ? link.Transport.ToString() : $"{link.Transport}+{string.Join(",", link.FallbackTransports)}",
                link.RemotePort.ToString(CultureInfo.InvariantCulture),
                link.Peer.SigningPublicKey is null ? _localizer.Get("Cli.No") : _localizer.Get("Cli.Yes"),
                link.Id.ToString());
        }

        return table.Render();
    }

    /// <summary>
    /// Removes a link by identifier or name.
    /// </summary>
    /// <param name="idOrName">The identifier or name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A summary.</returns>
    /// <exception cref="KeyNotFoundException">No link matches.</exception>
    public async Task<string> RemoveAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var link = await FindAsync(_links, idOrName, _localizer, cancellationToken);
        await _links.DeleteAsync(link.Id, cancellationToken);
        return _localizer.Format("Cli.Link.Removed", link.Name) + "\n";
    }

    /// <summary>
    /// Finds a link by identifier or exact name.
    /// </summary>
    /// <param name="links">The link repository.</param>
    /// <param name="idOrName">The identifier or name.</param>
    /// <param name="localizer">The localizer of the message when no link matches.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The link.</returns>
    /// <exception cref="KeyNotFoundException">No link matches.</exception>
    public static async Task<ChainLinkConfig> FindAsync(IChainLinkRepository links, string idOrName, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(idOrName, out var id) && await links.GetAsync(id, cancellationToken) is { } byId)
        {
            return byId;
        }

        return (await links.GetAllAsync(cancellationToken)).FirstOrDefault(link => string.Equals(link.Name, idOrName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException(localizer.Format("Cli.Link.NotFound", idOrName));
    }
}
