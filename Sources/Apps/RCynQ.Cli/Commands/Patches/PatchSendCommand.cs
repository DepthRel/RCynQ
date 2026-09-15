using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch send</c>: sends the local changes to the chain endpoint.
/// </summary>
[Command("patch send", Description = "Builds a patch from the sync base to the working tree and sends it to the counterpart.")]
public sealed class PatchSendCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchSendCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchSendCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<PatchCommandService>().SendAsync(cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("The patch was not applied.", 1);
        }
    }
}
