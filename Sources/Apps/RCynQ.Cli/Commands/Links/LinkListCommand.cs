using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Links;

/// <summary>
/// <c>rcynq link list</c>: lists the chain links.
/// </summary>
[Command("link list", Description = "Lists the chain links in chain order.")]
public sealed class LinkListCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkListCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public LinkListCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<LinkCommandService>().ListAsync(cancellationToken));
}
