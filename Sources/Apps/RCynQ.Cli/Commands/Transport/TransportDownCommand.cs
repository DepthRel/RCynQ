using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Transport;

/// <summary>
/// <c>rcynq transport down &lt;link&gt;</c>: stops a link.
/// </summary>
[Command("transport down", Description = "Stops the transport of a link.")]
public sealed class TransportDownCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransportDownCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public TransportDownCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the link identifier or name.</summary>
    [CommandParameter(0, Name = "link", Description = "Link identifier or name.")]
    public string Link { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<TransportCommandService>().DownAsync(Link, cancellationToken));
}
