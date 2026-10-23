using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Commits;

/// <summary>
/// <c>rcynq commits list</c>: lists the commits of the current branch the Origin does not have yet.
/// </summary>
[Command("commits list", Description = "Lists the commits of the current branch the Origin does not have yet, newest first (Workstation).")]
public sealed class CommitListCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CommitListCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public CommitListCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<CommitCommandService>().ListAsync(cancellationToken));
}
