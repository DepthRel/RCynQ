namespace RCynQ.Core.Config;

/// <summary>
/// Tells which repository the node works with now.
/// </summary>
/// <remarks>
/// Everything that needs the working tree (patches, the sync base, the Git channel, diagnostics) asks here each time,
/// so switching to another repository takes effect without a restart.
/// </remarks>
public interface IActiveRepository
{
    /// <summary>
    /// Gets the repository the node works with, or <see langword="null"/> when it has none (a Relay, or a node whose
    /// repositories are not set up yet).
    /// </summary>
    RepositoryEntry? Current { get; }

    /// <summary>
    /// Gets the repositories of the node, in the order of the configuration.
    /// </summary>
    IReadOnlyList<RepositoryEntry> Entries { get; }
}
