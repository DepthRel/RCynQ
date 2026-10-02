using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Git;

/// <summary>
/// <c>rcynq git fetch</c>: fetches branches from Origin through the chain.
/// </summary>
[Command("git fetch", Description = "Fetches all branches from Origin into refs/remotes/rcynq/* (Workstation only; never pushes).")]
public sealed class GitFetchCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitFetchCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public GitFetchCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        var (text, succeeded) = await context.Get<GitCommandService>().FetchAsync(cancellationToken);
        await console.Output.WriteAsync(text);
        if (!succeeded)
        {
            throw new CommandException("git fetch failed.", 1);
        }
    }
}
