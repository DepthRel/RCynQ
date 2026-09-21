using RCynQ.Git.Paths;

namespace RCynQ.Git.Manifest;

/// <summary>
/// Defines which directories are excluded from working tree snapshots.
/// </summary>
public sealed class SnapshotFilter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotFilter"/> class.
    /// </summary>
    /// <param name="excludedDirectoryNames">Directory names excluded at any depth. Git metadata is always excluded.</param>
    public SnapshotFilter(IEnumerable<string>? excludedDirectoryNames = null)
    {
        ExcludedDirectoryNames = new HashSet<string>(
            (excludedDirectoryNames ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets the excluded directory names (case-insensitive).
    /// </summary>
    public IReadOnlySet<string> ExcludedDirectoryNames { get; }

    /// <summary>
    /// Checks whether a directory with the given name is excluded.
    /// </summary>
    /// <param name="name">The directory name.</param>
    /// <returns><see langword="true"/> when excluded.</returns>
    public bool IsDirectoryExcluded(string name) => PathUtility.IsGitMetadataName(name) || ExcludedDirectoryNames.Contains(name);

    /// <summary>
    /// Checks whether a file with the given name is excluded (a <c>.git</c> file of a submodule or worktree).
    /// </summary>
    /// <param name="name">The file name.</param>
    /// <returns><see langword="true"/> when excluded.</returns>
    public bool IsFileExcluded(string name) => PathUtility.IsGitMetadataName(name);
}
