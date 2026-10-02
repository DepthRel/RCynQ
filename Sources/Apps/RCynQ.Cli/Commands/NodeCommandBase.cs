using CliFx;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Cli.Commands;

/// <summary>
/// Base class of commands that operate on the configured node.
/// </summary>
/// <remarks>
/// Domain failures become exit code <c>1</c> with the error message; invalid input becomes exit code <c>2</c>.
/// </remarks>
public abstract class NodeCommandBase : ICommand
{
    /// <summary>The node context factory.</summary>
    private readonly INodeContextFactory _contexts;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeCommandBase"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    protected NodeCommandBase(INodeContextFactory contexts)
    {
        _contexts = contexts;
    }

    /// <summary>
    /// Gets or sets the configuration file path.
    /// </summary>
    [CommandOption("config", 'c', Description = "Path of syncbridge.json (default: RCYNQ_CONFIG or ./syncbridge.json).")]
    public string? ConfigPath { get; set; }

    /// <inheritdoc />
    public async ValueTask ExecuteAsync(IConsole console)
    {
        var cancellationToken = console.RegisterCancellationHandler();
        await using var context = await _contexts.CreateAsync(ConfigPath, cancellationToken);
        try
        {
            await ExecuteAsync(context, console, cancellationToken);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException or FileNotFoundException)
        {
            throw new CommandException(context.Get<ILocalizer>().Describe(ex), 2);
        }
        catch (Exception ex) when (ex is SyncBridgeException or InvalidOperationException or IOException)
        {
            throw new CommandException(context.Get<ILocalizer>().Describe(ex), 1);
        }
    }

    /// <summary>
    /// Executes the command on the node.
    /// </summary>
    /// <param name="context">The node context.</param>
    /// <param name="console">The console.</param>
    /// <param name="cancellationToken">The cancellation token (Ctrl+C).</param>
    /// <returns>A task representing the command.</returns>
    protected abstract ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken);
}
