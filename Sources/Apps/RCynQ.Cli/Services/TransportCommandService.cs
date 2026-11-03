using RCynQ.Cli.Output;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Cli.Services;

/// <summary>
/// Starts, stops and reports transport links.
/// </summary>
public sealed class TransportCommandService
{
    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The link manager.</summary>
    private readonly ILinkManager _manager;

    /// <summary>The localizer of the output.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="manager">The link manager.</param>
    public TransportCommandService(ILocalizer localizer, IChainLinkRepository links, ILinkManager manager)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = links;
        _manager = manager;
    }

    /// <summary>
    /// Starts a link with failover.
    /// </summary>
    /// <param name="idOrName">The link identifier or name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state and whether it is usable.</returns>
    /// <exception cref="KeyNotFoundException">No link matches.</exception>
    public async Task<(string Text, bool Usable)> UpAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var link = await LinkCommandService.FindAsync(_links, idOrName, _localizer, cancellationToken);
        var state = await _manager.StartAsync(link.Id, cancellationToken);
        return (Describe(state), state.IsUsable);
    }

    /// <summary>
    /// Stops a link.
    /// </summary>
    /// <param name="idOrName">The link identifier or name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state.</returns>
    /// <exception cref="KeyNotFoundException">No link matches.</exception>
    public async Task<string> DownAsync(string idOrName, CancellationToken cancellationToken = default)
    {
        var link = await LinkCommandService.FindAsync(_links, idOrName, _localizer, cancellationToken);
        return Describe(await _manager.StopAsync(link.Id, cancellationToken));
    }

    /// <summary>
    /// Reports the state of all links.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The table.</returns>
    public async Task<string> StatusAsync(CancellationToken cancellationToken = default)
    {
        var states = await _manager.GetStatusAsync(cancellationToken);
        if (states.Count == 0)
        {
            return _localizer.Get("Cli.Link.None") + "\n";
        }

        var table = new ConsoleTable(_localizer.Get("Cli.Column.Name"), _localizer.Get("Cli.Column.Transport"), _localizer.Get("Cli.Column.Status"), _localizer.Get("Cli.Column.Message"));
        foreach (var state in states)
        {
            table.AddRow(state.LinkName, state.Kind.ToString(), state.Status.ToString(), state.Message);
        }

        return table.Render();
    }

    /// <summary>
    /// Describes a link state in one line.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <returns>The text.</returns>
    private string Describe(LinkState state) => _localizer.Format("Cli.Transport.State", state.LinkName, state.Status, state.Kind, state.Message) + "\n";
}
