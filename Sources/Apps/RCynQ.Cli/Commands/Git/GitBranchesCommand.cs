using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Git;

/// <summary>
/// <c>rcynq git branches</c>: lists the local branches and the branches fetched from Origin.
/// </summary>
[Command("git branches", Description = "Lists the local branches and the branches fetched from Origin, marking the current one (Workstation only).")]
public sealed class GitBranchesCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitBranchesCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public GitBranchesCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<GitCommandService>().ListBranchesAsync(cancellationToken));
}
