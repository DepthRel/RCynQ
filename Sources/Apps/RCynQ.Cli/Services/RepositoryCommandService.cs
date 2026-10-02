using System.Text;
using RCynQ.Sync.Repositories;

namespace RCynQ.Cli.Services;

/// <summary>
/// The <c>rcynq repo</c> commands: listing, adding, removing and switching the repositories of the node.
/// </summary>
/// <remarks>
/// The commands edit the configuration file of the node. A service that is running reads the file at start, so it has to
/// be restarted to see a change made here; the user interface makes the same changes without a restart.
/// </remarks>
public sealed class RepositoryCommandService
{
    /// <summary>The note printed after a change.</summary>
    private const string RESTART_NOTE = "Restart the RCynQ service of this node, if it is running, to apply the change.\n";

    /// <summary>The repository operations.</summary>
    private readonly RepositoryService _repositories;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryCommandService"/> class.
    /// </summary>
    /// <param name="repositories">The repository operations.</param>
    public RepositoryCommandService(RepositoryService repositories)
    {
        _repositories = repositories ?? throw new ArgumentNullException(nameof(repositories));
    }

    /// <summary>
    /// Lists the repositories of the node.
    /// </summary>
    /// <param name="askOrigin">Whether a Workstation asks the Origin for what it offers.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text for the console.</returns>
    public async Task<string> ListAsync(bool askOrigin, CancellationToken cancellationToken = default)
    {
        var list = await _repositories.ListAsync(askOrigin, cancellationToken);
        var text = new StringBuilder();
        if (list.Repositories.Count == 0)
        {
            text.Append("No repositories.\n");
        }

        foreach (var repository in list.Repositories)
        {
            text.Append(repository.IsActive ? "* " : "  ")
                .Append(repository.Name.Length > 0 ? repository.Name : "(not bound yet)")
                .Append("  ")
                .Append(repository.Path ?? "(not on this computer yet)")
                .Append('\n');
        }

        if (list.CatalogError is not null)
        {
            text.Append("The Origin was not asked: ").Append(list.CatalogError).Append('\n');
        }

        return text.ToString();
    }

    /// <summary>
    /// Adds a repository to the node.
    /// </summary>
    /// <param name="name">The name the nodes call the repository by.</param>
    /// <param name="path">The local folder.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text for the console.</returns>
    public async Task<string> AddAsync(string name, string path, CancellationToken cancellationToken = default)
    {
        var entry = await _repositories.AddAsync(name, path, cancellationToken);
        return $"Added repository '{entry.Name}' at {entry.Path}.\n{RESTART_NOTE}";
    }

    /// <summary>
    /// Removes a repository from the node; its files stay.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text for the console.</returns>
    public async Task<string> RemoveAsync(string name, CancellationToken cancellationToken = default)
    {
        await _repositories.RemoveAsync(name, cancellationToken);
        return $"Removed repository '{name}' from this node. Its folder and files were not touched.\n{RESTART_NOTE}";
    }

    /// <summary>
    /// Makes a repository the one the node works with.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text for the console.</returns>
    public async Task<string> UseAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry = await _repositories.ActivateAsync(name, cancellationToken);
        return $"The node works with repository '{entry.Name}' at {entry.Path}.\n{RESTART_NOTE}";
    }
}
