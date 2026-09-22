using RCynQ.Core.Exceptions;
using RCynQ.Core.State;

namespace RCynQ.Git.Repository;

/// <summary>
/// Git channel operations: Source receives branches from Dest; push is never performed.
/// </summary>
public interface IGitRepositoryService
{
    /// <summary>
    /// Checks whether a directory is inside a Git working tree.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the directory is a working tree.</returns>
    Task<bool> IsWorkTreeAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches all branches from a remote URL into <c>refs/remotes/&lt;remoteName&gt;/*</c>.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteUrl">The remote URL (<c>ssh://</c> or <c>file://</c> or an absolute path).</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="sshCommand">The value for <c>GIT_SSH_COMMAND</c>, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The git output describing updated references.</returns>
    /// <exception cref="ArgumentException">The URL or remote name is unsafe.</exception>
    /// <exception cref="GitExecutionException">The fetch failed.</exception>
    Task<string> FetchAsync(string directory, string remoteUrl, string remoteName, string? sshCommand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fast-forwards the current branch to its remote-tracking reference.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The git output.</returns>
    /// <exception cref="GitExecutionException">The branch cannot be fast-forwarded.</exception>
    Task<string> PullFastForwardAsync(string directory, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current branch and its divergence from the remote-tracking reference.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branch information.</returns>
    Task<GitBranchInfo> GetBranchInfoAsync(string directory, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists files with unresolved merge conflicts.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The relative paths.</returns>
    Task<IReadOnlyList<string>> GetUnmergedFilesAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the branch the working tree has checked out, also when it has no commit yet.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branch name, or <see langword="null"/> for a detached HEAD or a directory that is no working tree.</returns>
    Task<string?> GetCurrentBranchAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether the working tree has at least one commit.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when HEAD points to a commit.</returns>
    Task<bool> HasCommitsAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the commits a peer can name as "I already have this": HEAD, the local branches and the remote-tracking branches.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The distinct full object names; empty for a directory that is no repository or has no commit.</returns>
    Task<IReadOnlyList<string>> GetKnownCommitsAsync(string directory, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps only the commits that exist in the repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="commits">The claimed full object names.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commits that exist. Malformed names are dropped.</returns>
    Task<IReadOnlyList<string>> FilterExistingCommitsAsync(string directory, IEnumerable<string> commits, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a bundle with every branch of the repository, leaving out what is reachable from the given commits.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="bundlePath">The file to create.</param>
    /// <param name="have">The commits the receiver already has; they must exist in the repository.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the bundle was created; <see langword="false"/> when the receiver has everything and no bundle is needed.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<bool> CreateBundleAsync(string directory, string bundlePath, IReadOnlyCollection<string> have, CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches the branches of a bundle file into the remote-tracking namespace, without removing remote-tracking branches the bundle does not mention.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="bundlePath">The bundle file.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The git output.</returns>
    /// <exception cref="GitExecutionException">git reported an error, for example missing prerequisite commits.</exception>
    Task<string> FetchBundleAsync(string directory, string bundlePath, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an empty repository in a directory.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="initialBranch">The name of the first branch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the repository exists.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task InitializeAsync(string directory, string initialBranch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes sure a git remote with the given name is configured, so that ordinary Git tooling (<c>git checkout</c>,
    /// IDE branch pickers) recognizes <c>refs/remotes/&lt;remoteName&gt;/*</c> as belonging to a remote and can create local
    /// tracking branches from them on its own.
    /// </summary>
    /// <remarks>
    /// RCynQ never fetches through this remote: <see cref="FetchAsync"/> and <see cref="FetchBundleAsync"/> always pass an explicit
    /// URL or bundle file and ignore whatever is configured here. The remote exists purely so other tools stop treating the fetched
    /// branches as anonymous refs; its URL is a placeholder that is never dereferenced by RCynQ, and fetching from it directly (for
    /// example plain <c>git fetch &lt;remoteName&gt;</c> or <c>git pull</c>) fails on purpose.
    /// </remarks>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="placeholderUrl">The URL to store if the remote does not exist yet.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the remote exists.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task EnsureRemoteAsync(string directory, string remoteName, string placeholderUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Points a repository without any commit at a fetched remote branch.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <param name="branch">The branch to adopt.</param>
    /// <param name="keepWorkingFiles">
    /// <see langword="true"/> to leave the files of the directory as they are, so that differences to the branch show up as local
    /// changes; <see langword="false"/> to check out the branch (for an empty directory).
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the branch is adopted.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task AdoptRemoteBranchAsync(string directory, string remoteName, string branch, bool keepWorkingFiles, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the full name of the commit HEAD points to.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commit name, or <see langword="null"/> when there is no commit or the directory is no working tree.</returns>
    Task<string?> GetHeadCommitAsync(string directory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files that differ between two commits.
    /// </summary>
    /// <param name="directory">The working tree, which is the root of the repository.</param>
    /// <param name="fromCommit">The older commit.</param>
    /// <param name="toCommit">The newer commit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The changed files with paths relative to <paramref name="directory"/>; renames are reported as a deletion and an addition.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<IReadOnlyList<GitPathChange>> GetChangedPathsAsync(string directory, string fromCommit, string toCommit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports every file tracked at a commit-ish into a plain directory, with no Git metadata.
    /// </summary>
    /// <param name="directory">The repository the commit-ish is resolved in.</param>
    /// <param name="treeish">The commit, tag or ref to export, for example <c>refs/remotes/dest/main</c>.</param>
    /// <param name="destinationDirectory">The directory to extract into; created if it does not exist.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the directory holds the exported tree.</returns>
    /// <exception cref="ArgumentException"><paramref name="treeish"/> is unsafe.</exception>
    /// <exception cref="GitExecutionException">git reported an error, for example an unresolvable <paramref name="treeish"/>.</exception>
    Task ExportTreeAsync(string directory, string treeish, string destinationDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a commit-ish to the full object name of the commit it points at.
    /// </summary>
    /// <param name="directory">The repository the commit-ish is resolved in.</param>
    /// <param name="treeish">The commit, tag or ref to resolve.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full commit object name, or <see langword="null"/> when it does not resolve to a commit.</returns>
    /// <exception cref="ArgumentException"><paramref name="treeish"/> is unsafe.</exception>
    Task<string?> ResolveCommitAsync(string directory, string treeish, CancellationToken cancellationToken = default);
}

/// <summary>
/// A file that differs between two commits.
/// </summary>
/// <param name="Deleted"><see langword="true"/> when the file no longer exists in the newer commit.</param>
/// <param name="Path">The path relative to the root of the repository, with <c>/</c> separators.</param>
public sealed record GitPathChange(bool Deleted, string Path);
