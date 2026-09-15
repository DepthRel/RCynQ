using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands;

/// <summary>
/// <c>rcynq status</c>: node, links, synchronization state and recent patches.
/// </summary>
[Command("status", Description = "Shows the node, its links, the synchronization state and recent patches.")]
public sealed class StatusCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public StatusCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<StatusCommandService>().BuildAsync(5, cancellationToken));
}
