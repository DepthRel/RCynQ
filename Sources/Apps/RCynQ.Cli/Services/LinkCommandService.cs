using System.Globalization;
using RCynQ.Cli.Output;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkCommandService"/> class.
    /// </summary>
    /// <param name="links">The link repository.</param>
    /// <param name="store">The store that saves a typed link without erasing what is known about its peer.</param>
    public LinkCommandService(IChainLinkRepository links, ConfiguredLinkStore store)
    {
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
        return $"Saved link '{link.Name}' ({link.Id}) to {link.Peer.Role} {link.Peer.Id} at {link.Peer.Ssh.JumpSpec} via {link.Transport}.\n";
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
            return "No links are configured.\n";
        }

        var table = new ConsoleTable("Pos", "Name", "Peer", "Role", "Address", "Transport", "Remote", "Signed", "Id");
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
                link.Peer.SigningPublicKey is null ? "no" : "yes",
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
        var link = await FindAsync(_links, idOrName, cancellationToken);
        await _links.DeleteAsync(link.Id, cancellationToken);
        return $"Removed link '{link.Name}'.\n";
    }

    /// <summary>
    /// Finds a link by identifier or exact name.
    /// </summary>
    /// <param name="links">The link repository.</param>
    /// <param name="idOrName">The identifier or name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The link.</returns>
    /// <exception cref="KeyNotFoundException">No link matches.</exception>
    public static async Task<ChainLinkConfig> FindAsync(IChainLinkRepository links, string idOrName, CancellationToken cancellationToken)
    {
        if (Guid.TryParse(idOrName, out var id) && await links.GetAsync(id, cancellationToken) is { } byId)
        {
            return byId;
        }

        return (await links.GetAllAsync(cancellationToken)).FirstOrDefault(link => string.Equals(link.Name, idOrName, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"No link with identifier or name '{idOrName}'. Run 'link list'.");
    }
}
