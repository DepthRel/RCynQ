using RCynQ.Core.Validation;

namespace RCynQ.Core.Config;

/// <summary>
/// One repository the node works with: the name the nodes call it by and where it is kept on this computer.
/// </summary>
public sealed record RepositoryEntry
{
    /// <summary>
    /// The identifier of the repository a node started with. Its data lives directly in the data folder of the node, where
    /// earlier versions, which knew one repository only, kept it.
    /// </summary>
    public static readonly Guid DefaultId = Guid.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RepositoryEntry"/> record.
    /// </summary>
    /// <param name="id">The local identifier; <see cref="DefaultId"/> for the repository the node started with.</param>
    /// <param name="name">The name the nodes call the repository by, or an empty string when it is not bound yet.</param>
    /// <param name="path">The local path.</param>
    /// <exception cref="ArgumentException">The name is not valid or the path is blank.</exception>
    public RepositoryEntry(Guid id, string name, string path)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length > 0 && !RepositoryName.IsValid(name))
        {
            throw new ArgumentException($"Repository name \"{name}\" is not valid.", nameof(name));
        }

        Id = id;
        Name = name;
        Path = Guard.NotBlank(path, nameof(path));
    }

    /// <summary>
    /// Gets the local identifier, which the data of the node about this repository is kept under.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the name the nodes call the repository by; empty until a Workstation folder is bound to a repository of the Origin.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the local path: the Git repository on an Origin, the working copy on a Workstation.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets a value indicating whether the entry has a name, so the other node can be told which repository is meant.
    /// </summary>
    public bool IsBound => Name.Length > 0;

    /// <summary>
    /// Tells whether a name means this repository; names differ by more than letter case.
    /// </summary>
    /// <param name="name">The name; may be <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the entry is bound and has this name.</returns>
    public bool IsNamed(string? name) => IsBound && string.Equals(Name, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Returns the entry under a name.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <returns>The entry with the name.</returns>
    public RepositoryEntry WithName(string name) => new(Id, name, Path);
}
