using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch baseline</c>: initializes the sync base from the working tree.
/// </summary>
[Command("patch baseline", Description = "Creates the sync base from the working tree. Run on Source and Dest while both trees are identical.")]
public sealed class PatchBaselineCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchBaselineCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public PatchBaselineCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<PatchCommandService>().BaselineAsync(cancellationToken));
}
