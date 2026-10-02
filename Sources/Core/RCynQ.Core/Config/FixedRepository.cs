namespace RCynQ.Core.Config;

/// <summary>
/// An <see cref="IActiveRepository"/> that never changes: for hosts and tools that work with one repository only.
/// </summary>
public sealed class FixedRepository : IActiveRepository
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FixedRepository"/> class.
    /// </summary>
    /// <param name="current">The repository, or <see langword="null"/> for none.</param>
    public FixedRepository(RepositoryEntry? current) => Current = current;

    /// <inheritdoc />
    public RepositoryEntry? Current { get; }

    /// <inheritdoc />
    public IReadOnlyList<RepositoryEntry> Entries => Current is null ? Array.Empty<RepositoryEntry>() : new[] { Current };

    /// <summary>
    /// Creates the repository of a node from the path the node was configured with at start.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The unnamed default repository of the node, or none when the node has no working tree.</returns>
    public static FixedRepository FromNode(NodeConfig node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new FixedRepository(node.EffectiveWorkdir is { } path ? new RepositoryEntry(RepositoryEntry.DefaultId, string.Empty, path) : null);
    }
}
