using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Patches;

/// <summary>
/// <c>rcynq patch baseline</c>: takes a manual snapshot of the working tree as the sync base, for a working tree without Git.
/// </summary>
[Command("patch baseline", Description = "Rebuilds the sync base: for a Git working tree from its commit (forgetting what was exchanged), for a folder without Git as a manual snapshot of the working tree (run on Source and Dest while both trees are identical).")]
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
