using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Transport;

/// <summary>
/// <c>rcynq transport up &lt;link&gt;</c>: starts a link with failover.
/// </summary>
[Command("transport up", Description = "Starts the transport of a link, failing over to its fallback transports.")]
public sealed class TransportUpCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransportUpCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public TransportUpCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the link identifier or name.</summary>
    [CommandParameter(0, Name = "link", Description = "Link identifier or name.")]
    public string Link { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, usable) = await context.Get<TransportCommandService>().UpAsync(Link, cancellationToken);
        await console.Output.WriteAsync(text);
        if (!usable)
        {
            throw new CommandException("The link could not be started.", 1);
        }
    }
}
