using CliFx.Attributes;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Cli.Services;

namespace RCynQ.Cli.Commands.Repositories;

/// <summary>
/// <c>rcynq repo add</c>: an Origin registers a Git repository under a name; a Workstation says where it keeps the
/// repository of that name.
/// </summary>
[Command("repo add", Description = "Adds a repository: on an Origin a Git repository under a name, on a Workstation the folder for a repository the Origin offers.")]
public sealed class RepoAddCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RepoAddCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public RepoAddCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>Gets or sets the repository name.</summary>
    [CommandParameter(0, Name = "name", Description = "The name the nodes call the repository by: letters, digits, '.', '_' and '-'.")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the local folder.</summary>
    [CommandParameter(1, Name = "path", Description = "The local folder of the repository.")]
    public string RepositoryPath { get; set; } = string.Empty;

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
        => await console.Output.WriteAsync(await context.Get<RepositoryCommandService>().AddAsync(Name, RepositoryPath, cancellationToken));
}
