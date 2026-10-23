using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Commits;

/// <summary>
/// The Git operations of a delivery of commits: listing, packing and unpacking commits, checking them, moving a branch.
/// </summary>
public interface IGitCommitService
{
    /// <summary>
    /// Lists the commits on the first-parent chain after one commit up to another, oldest first.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="fromCommit">The commit the list starts after (not listed).</param>
    /// <param name="toCommit">The last commit of the list.</param>
    /// <param name="max">The most commits to read: the newest ones when there are more.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commits, oldest first.</returns>
    /// <exception cref="ArgumentException">A commit name is not safe.</exception>
    /// <exception cref="GitExecutionException">Git failed.</exception>
    Task<IReadOnlyList<CommitInfo>> ListFirstParentAsync(string directory, string fromCommit, string toCommit, int max, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a bundle with the commits after a base commit up to a tip commit.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="bundlePath">The bundle file to write.</param>
    /// <param name="baseCommit">The commit the receiver has.</param>
    /// <param name="tipCommit">The last commit of the bundle.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the bundle is written.</returns>
    /// <remarks>Git bundles only references, so the tip is named for the time of the call by a reference under <c>refs/rcynq/</c>, removed afterwards.</remarks>
    /// <exception cref="ArgumentException">A commit name is not full.</exception>
    /// <exception cref="GitExecutionException">Git failed.</exception>
    Task CreateBundleAsync(string directory, string bundlePath, string baseCommit, string tipCommit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unpacks a bundle into a quarantine: a separate object directory that reads the objects of the repository but keeps
    /// every new object outside it.
    /// </summary>
    /// <param name="directory">The working tree of the repository.</param>
    /// <param name="bundlePath">The bundle file.</param>
    /// <param name="quarantineDirectory">The empty folder of the quarantine; it is created.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The quarantine with the references the bundle carries.</returns>
    /// <exception cref="GitExecutionException">The bundle is not valid, or needs commits the repository does not have.</exception>
    Task<GitQuarantine> UnbundleAsync(string directory, string bundlePath, string quarantineDirectory, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the first-parent chain after a base commit up to a tip commit, oldest first.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="baseCommit">The commit the chain starts after.</param>
    /// <param name="tipCommit">The last commit of the chain.</param>
    /// <param name="quarantine">The quarantine that holds new objects, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The links of the chain; empty when the base is not an ancestor of the tip.</returns>
    /// <exception cref="ArgumentException">A commit name is not full.</exception>
    Task<IReadOnlyList<GitCommitLink>> GetFirstParentChainAsync(string directory, string baseCommit, string tipCommit, GitQuarantine? quarantine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every path the commits after a base commit up to a tip commit add, change or delete, also through the
    /// commits a merge brings in.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="baseCommit">The commit the range starts after.</param>
    /// <param name="tipCommit">The last commit of the range.</param>
    /// <param name="quarantine">The quarantine that holds new objects, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The distinct paths, with <c>/</c> separators.</returns>
    /// <exception cref="ArgumentException">A commit name is not full.</exception>
    /// <exception cref="GitExecutionException">Git failed.</exception>
    Task<IReadOnlyCollection<string>> GetTouchedPathsAsync(string directory, string baseCommit, string tipCommit, GitQuarantine? quarantine, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the packs of a quarantine into the object directory of the repository.
    /// </summary>
    /// <param name="quarantine">The quarantine.</param>
    /// <returns>The files now in the repository, so that <see cref="RemovePacks"/> can take them back out.</returns>
    /// <exception cref="IOException">A file could not be moved; the files moved so far are taken back out.</exception>
    IReadOnlyList<string> MovePacks(GitQuarantine quarantine);

    /// <summary>
    /// Removes pack files that <see cref="MovePacks"/> put into a repository and nothing refers to yet.
    /// </summary>
    /// <param name="files">The files.</param>
    void RemovePacks(IReadOnlyList<string> files);

    /// <summary>
    /// Moves the checked out branch forward to a commit, with the index and the working tree.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="tipCommit">The commit.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The output of Git.</returns>
    /// <exception cref="ArgumentException">The commit name is not full.</exception>
    /// <exception cref="GitExecutionException">The branch cannot be fast-forwarded.</exception>
    Task<string> FastForwardAsync(string directory, string tipCommit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves the checked out branch back to a commit, as <c>git reset</c> does.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <param name="targetCommit">The commit that becomes <c>HEAD</c>.</param>
    /// <param name="mode">What happens to the changes of the dropped commits.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the branch moved.</returns>
    /// <exception cref="ArgumentException">The commit name is not full or the mode is not defined.</exception>
    /// <exception cref="GitExecutionException">Git failed.</exception>
    Task ResetAsync(string directory, string targetCommit, CommitResetMode mode, CancellationToken cancellationToken = default);
}
