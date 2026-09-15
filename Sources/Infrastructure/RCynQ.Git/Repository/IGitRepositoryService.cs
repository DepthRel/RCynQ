using System.Text.RegularExpressions;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Core.Validation;

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
}
