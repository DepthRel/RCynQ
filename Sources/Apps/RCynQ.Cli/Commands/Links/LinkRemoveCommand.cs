using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Links;

/// <summary>
/// <c>rcynq link remove &lt;id|name&gt;</c>: removes a chain link (the peer and its keys are kept).
/// </summary>
[Command("link remove", Description = "Removes a chain link by identifier or name; the peer and its keys are kept.")]
public sealed class LinkRemoveCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRemoveCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public LinkRemoveCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the link identifier or name.</summary>
    [CommandParameter(0, Name = "link", Description = "Link identifier or name.")]
    public string Link { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<LinkCommandService>().RemoveAsync(Link, cancellationToken));
}
