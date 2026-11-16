using RCynQ.Core.Exceptions;
using RCynQ.Core.State;

namespace RCynQ.Git.Repository;

/// <summary>
/// The branches of a Workstation working copy: which ones it can be switched to, the switch itself, and the branch the
/// Origin has checked out. Only the Workstation working copy is touched; nothing here runs on an Origin.
/// </summary>
public interface IGitBranchService
{
    /// <summary>
    /// Lists the local branches and the remote-tracking branches of a namespace, one row per name, with how each stands
    /// to the remote-tracking branch of the same name.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace of the Origin.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branches ordered by name (ordinal); names that are not safe to pass to Git are left out.</returns>
    /// <exception cref="ArgumentException">The remote name is unsafe.</exception>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<IReadOnlyList<GitSwitchableBranch>> ListSwitchableAsync(string directory, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Switches the working tree to a branch: a local one, or a remote-tracking one that becomes a local branch of the same
    /// name. Uncommitted changes are carried over as a plain <c>git switch</c> carries them; when Git cannot, it refuses and
    /// nothing changes.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace of the Origin.</param>
    /// <param name="branch">The branch name.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended.</returns>
    /// <exception cref="ArgumentException">The branch or remote name is unsafe, or neither a local nor a remote-tracking branch has it.</exception>
    Task<GitSwitchAttempt> SwitchAsync(string directory, string remoteName, string branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the commits that only a detached <c>HEAD</c> reaches: no branch, remote-tracking branch or tag does.
    /// A switch would leave them unreachable.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="max">The most commits to list.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full commit names, newest first; empty when <c>HEAD</c> is on a branch or has no commit.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<IReadOnlyList<string>> GetCommitsOnlyInHeadAsync(string directory, int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Counts the commits of <c>HEAD</c> that no remote-tracking branch of a namespace has: the commits the Origin does not have.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace of the Origin.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The number; 0 for a repository without commits.</returns>
    /// <exception cref="ArgumentException">The remote name is unsafe.</exception>
    Task<int> CountCommitsNotOnRemoteAsync(string directory, string remoteName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the branch the Origin has checked out as <c>refs/remotes/&lt;remoteName&gt;/HEAD</c>, as Git records
    /// <c>origin/HEAD</c>; removes the record when the branch is not known.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace of the Origin.</param>
    /// <param name="branch">The checked out branch; untrusted. <see langword="null"/> for a detached <c>HEAD</c>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the record is written.</returns>
    /// <exception cref="ArgumentException">The remote name is unsafe.</exception>
    Task SetRemoteHeadAsync(string directory, string remoteName, string? branch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the branch recorded by <see cref="SetRemoteHeadAsync"/>.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="remoteName">The remote-tracking namespace of the Origin.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branch name, or <see langword="null"/> when none is recorded.</returns>
    /// <exception cref="ArgumentException">The remote name is unsafe.</exception>
    Task<string?> GetRemoteHeadAsync(string directory, string remoteName, CancellationToken cancellationToken = default);
}
