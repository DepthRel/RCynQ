using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch list</c>: lists recent patches.
/// </summary>
[Command("patch list", Description = "Lists recent incoming and outgoing patches.")]
public sealed class PatchListCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchListCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchListCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the number of patches.</summary>
    [CommandOption("take", 'n', Description = "Number of patches to show.")]
    public int Take { get; set; } = 20;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<PatchCommandService>().ListAsync(Take, cancellationToken));
}
