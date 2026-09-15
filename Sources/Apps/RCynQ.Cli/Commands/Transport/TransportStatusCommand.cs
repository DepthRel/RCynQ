using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Transport;

/// <summary>
/// <c>rcynq transport status</c>: probes every link.
/// </summary>
[Command("transport status", Description = "Probes and shows the state of every link.")]
public sealed class TransportStatusCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TransportStatusCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public TransportStatusCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<TransportCommandService>().StatusAsync(cancellationToken));
}
