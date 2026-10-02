using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Repositories;

/// <summary>
/// <c>rcynq repo list</c>: lists the repositories of the node; the one it works with is marked with <c>*</c>.
/// </summary>
[Command("repo list", Description = "Lists the repositories of the node; '*' marks the one it works with.")]
public sealed class RepoListCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RepoListCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public RepoListCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets a value indicating whether the Origin is left alone.</summary>
    [CommandOption("local", Description = "On a Workstation, do not ask the Origin which repositories it offers.")]
    public bool LocalOnly { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<RepositoryCommandService>().ListAsync(!LocalOnly, cancellationToken));
}
