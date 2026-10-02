namespace RCynQ.Core.Config;

/// <summary>
/// One repository of the node as written in the configuration file.
/// </summary>
/// <remarks>
/// On an Origin an entry registers a Git repository under a name; on a Workstation it says in which local folder the
/// repository of that name is kept. Use <see cref="SyncBridgeOptionsValidator"/> to validate entries and to map them
/// to <see cref="RepositoryEntry"/>.
/// </remarks>
public sealed class RepositoryOptions
{
    /// <summary>
    /// Gets or sets the local identifier of the entry, which the data of the node is kept under. Empty for the
    /// repository the node started with.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the name the nodes call the repository by. Empty on a Workstation whose folder has not been bound
    /// to a repository of the Origin yet.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the local path: the Git repository on an Origin, the working copy on a Workstation.
    /// </summary>
    public string Path { get; set; } = string.Empty;
}
