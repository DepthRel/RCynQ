using RCynQ.Git.Paths;

namespace RCynQ.Git.Manifest;

/// <summary>
/// Defines which files and directories are excluded from working tree snapshots.
/// </summary>
/// <remarks>
/// Two mechanisms apply together: a list of directory names excluded at any depth, and the <c>.gitignore</c> files found in
/// the tree. Git metadata is always excluded.
/// </remarks>
public sealed class SnapshotFilter
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotFilter"/> class.
    /// </summary>
    /// <param name="excludedDirectoryNames">Directory names excluded at any depth. Git metadata is always excluded.</param>
    /// <param name="respectGitIgnore">Whether the <c>.gitignore</c> files of the tree exclude the files and directories they describe.</param>
    /// <param name="gitIgnoreIgnoreCase">
    /// Whether <c>.gitignore</c> patterns match without regard to case; <see langword="null"/> uses the Git default of the
    /// platform (case-insensitive on Windows and macOS).
    /// </param>
    public SnapshotFilter(IEnumerable<string>? excludedDirectoryNames = null, bool respectGitIgnore = true, bool? gitIgnoreIgnoreCase = null)
    {
        ExcludedDirectoryNames = new HashSet<string>(
            (excludedDirectoryNames ?? Array.Empty<string>()).Where(name => !string.IsNullOrWhiteSpace(name)).Select(name => name.Trim()),
            StringComparer.OrdinalIgnoreCase);
        RespectGitIgnore = respectGitIgnore;
        GitIgnoreIgnoreCase = gitIgnoreIgnoreCase ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());
    }

    /// <summary>
    /// Gets the excluded directory names (case-insensitive).
    /// </summary>
    public IReadOnlySet<string> ExcludedDirectoryNames { get; }

    /// <summary>
    /// Gets a value indicating whether the <c>.gitignore</c> files of the tree exclude files and directories.
    /// </summary>
    public bool RespectGitIgnore { get; }

    /// <summary>
    /// Gets a value indicating whether <c>.gitignore</c> patterns match without regard to case.
    /// </summary>
    public bool GitIgnoreIgnoreCase { get; }

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
