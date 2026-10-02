using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Repositories;

/// <summary>
/// <c>rcynq repo use</c>: makes a repository the one the node works with.
/// </summary>
[Command("repo use", Description = "Makes a repository the one the node works with.")]
public sealed class RepoUseCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RepoUseCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public RepoUseCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the repository name.</summary>
    [CommandParameter(0, Name = "name", Description = "The repository name.")]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<RepositoryCommandService>().UseAsync(Name, cancellationToken));
}
