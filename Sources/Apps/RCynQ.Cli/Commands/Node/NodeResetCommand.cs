using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using Microsoft.Data.Sqlite;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;

namespace RCynQ.Cli.Commands.Node;

/// <summary>
/// <c>rcynq node reset</c>: removes the configuration and the data of the node from this computer, so the next start of
/// RCynQ begins with the first setup. It tells what goes and what stays, and does nothing until the user names the node.
/// </summary>
[Command("node reset", Description = "Removes the configuration and all data of the node from this computer (its keys, database, patches, history and logs, and its lines in authorized_keys). The repositories stay. Asks to type the node identifier first.")]
public sealed class NodeResetCommand : ICommand
{
    /// <summary>
    /// The word typed to confirm the reset of a node whose configuration has no identifier.
    /// </summary>
    private const string FALLBACK_CONFIRMATION = "reset";

    /// <summary>The node context factory, which opens the database to find the lines of the node in authorized_keys.</summary>
    private readonly INodeContextFactory _contexts;

    /// <summary>Removes the configuration and the data.</summary>
    private readonly INodeDataEraser _eraser;

    /// <summary>Tells whether the service of the node runs.</summary>
    private readonly ILocalServiceProbe _service;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeResetCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    /// <param name="eraser">Removes the configuration and the data.</param>
    /// <param name="service">Tells whether the service of the node runs.</param>
    /// <param name="localizer">The localizer of the messages.</param>
    public NodeResetCommand(INodeContextFactory contexts, INodeDataEraser eraser, ILocalServiceProbe service, ILocalizer localizer)
    {
        _contexts = contexts ?? throw new ArgumentNullException(nameof(contexts));
        _eraser = eraser ?? throw new ArgumentNullException(nameof(eraser));
        _service = service ?? throw new ArgumentNullException(nameof(service));
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <summary>
    /// Gets or sets the configuration file path.
    /// </summary>
    [CommandOption("config", 'c', Description = "Path of syncbridge.json (default: RCYNQ_CONFIG or ./syncbridge.json).")]
    public string? ConfigPath { get; set; }

    /// <summary>
    /// Gets or sets the node identifier given to confirm the reset without a question, for scripts.
    /// </summary>
    [CommandOption("confirm", Description = "The identifier of the node, to confirm the reset without the question.")]
    public string? Confirmation { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var cancellationToken = console.RegisterCancellationHandler();

        var path = ConfigurationFileEditor.ResolvePath(ConfigPath);
        SyncBridgeOptions options;
        try
        {
            options = await ConfigurationFileEditor.LoadAsync(path, cancellationToken);
        }
        catch (SyncBridgeException ex)
        {
            throw new CommandException(_localizer.Describe(ex), 2);
        }

        var plan = _eraser.Plan(options, path, ConfigLocations.FindPortableRoot());
        var word = string.IsNullOrWhiteSpace(plan.NodeId) ? FALLBACK_CONFIRMATION : plan.NodeId;

        // The service holds the database open and would write it again: it has to end first.
        if (await _service.IsRunningAsync(options.ListenPort, cancellationToken))
        {
            throw new CommandException(_localizer.Format("Cli.Reset.ServiceRunning", options.ListenPort), 1);
        }

        await console.Output.WriteAsync(Describe(plan, options));
        await ConfirmAsync(console, word);

        await console.Output.WriteLineAsync(await RevokeAccessAsync(cancellationToken));
        var report = _eraser.Erase(plan);
        if (!report.IsComplete)
        {
            throw new CommandException(
                _localizer.Format("Cli.Reset.Incomplete", report.Removed) + Environment.NewLine + string.Join(Environment.NewLine, report.Failed.Select(item => " - " + item)),
                1);
        }

        await console.Output.WriteLineAsync(_localizer.Format("Cli.Reset.Done", word, report.Removed));
    }

    /// <summary>
    /// Words what the reset removes and what stays.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="options">The options of the node.</param>
    /// <returns>The text, ending with a line break.</returns>
    private string Describe(NodeResetPlan plan, SyncBridgeOptions options)
    {
        var lines = new List<string>
        {
            _localizer.Format("Cli.Reset.Header", plan.NodeId, options.Role),
            "  - " + _localizer.Get("Reset.Config"),
            "    " + plan.ConfigPath,
            "  - " + _localizer.Get(plan.HasData ? "Reset.Data" : "Reset.NoData"),
            "    " + plan.DataRoot,
            "  - " + _localizer.Get("Reset.Access"),
            _localizer.Get("Reset.Stays"),
            _localizer.Get("Reset.Neighbours"),
            _localizer.Get("Reset.Irreversible"),
        };
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    /// <summary>
    /// Makes sure the user named the node: by <c>--confirm</c>, or by typing it when asked.
    /// </summary>
    /// <param name="console">The console.</param>
    /// <param name="word">The word that confirms.</param>
    /// <returns>A task that completes when the reset is confirmed.</returns>
    /// <exception cref="CommandException">The reset is not confirmed; nothing was removed.</exception>
    private async Task ConfirmAsync(IConsole console, string word)
    {
        if (Confirmation is not null)
        {
            if (!string.Equals(Confirmation.Trim(), word, StringComparison.Ordinal))
            {
                throw new CommandException(_localizer.Format("Cli.Reset.Mismatch", Confirmation.Trim(), word), 2);
            }

            return;
        }

        await console.Output.WriteAsync(_localizer.Format("Cli.Reset.Prompt", word) + " ");
        var answer = await console.Input.ReadLineAsync();
        if (!string.Equals(answer?.Trim(), word, StringComparison.Ordinal))
        {
            throw new CommandException(_localizer.Format("Cli.Reset.Cancelled", word), 1);
        }
    }

    /// <summary>
    /// Takes the lines of the node out of the local <c>authorized_keys</c>. A node whose configuration or database cannot
    /// be opened is still reset: the reset is also the way out of such a state.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The line that tells what became of the lines.</returns>
    private async Task<string> RevokeAccessAsync(CancellationToken cancellationToken)
    {
        AccessRevocationResult result;
        try
        {
            await using (var context = await _contexts.CreateAsync(ConfigPath, cancellationToken))
            {
                result = await context.Get<PeerAccessService>().RevokeAsync(cancellationToken);
            }
        }
        catch (Exception ex) when (ex is CommandException or SyncBridgeException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return _localizer.Format("Reset.AccessFailed", ex is CommandException command ? command.Message : _localizer.Describe(ex));
        }
        finally
        {
            // The pool of SQLite keeps the database file open after the context is gone, and an open file cannot be removed.
            SqliteConnection.ClearAllPools();
        }

        return result.NeedsElevation
            ? _localizer.Format("Reset.AccessNeedsAdmin", result.FilePath)
            : _localizer.Format("Reset.AccessRemoved", result.Removed, result.FilePath);
    }
}
