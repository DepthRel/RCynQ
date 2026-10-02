using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Repositories;

/// <summary>
/// <c>rcynq repo remove</c>: removes a repository from the list of the node; its folder and files stay.
/// </summary>
[Command("repo remove", Description = "Removes a repository from the list of the node; its folder and files are not touched.")]
public sealed class RepoRemoveCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RepoRemoveCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public RepoRemoveCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the repository name.</summary>
    [CommandParameter(0, Name = "name", Description = "The repository name.")]
    public string Name { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<RepositoryCommandService>().RemoveAsync(Name, cancellationToken));
}
