using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Sync.SyncBase;

/// <summary>
/// The Git commit a node's sync base follows, or why there is none.
/// </summary>
/// <param name="IsRepository">Whether the working tree is a Git repository.</param>
/// <param name="Branch">The branch the base follows, or <see langword="null"/> for a detached HEAD or when there is no commit.</param>
/// <param name="Commit">The full name of the commit, or <see langword="null"/> when there is none.</param>
/// <param name="Problem">Why there is no commit, in the language of the request; <see langword="null"/> when there is one.</param>
public sealed record GitSyncBaseTarget(bool IsRepository, string? Branch, string? Commit, string? Problem);

/// <summary>
/// Finds the Git commit a node's sync base follows.
/// </summary>
public interface IGitSyncBaseLocator
{
    /// <summary>
    /// Finds the commit.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commit, or why there is none.</returns>
    Task<GitSyncBaseTarget> LocateAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IGitSyncBaseLocator"/>: the commit both nodes are known to have.
/// </summary>
/// <remarks>
/// <para>
/// On Dest it is <c>HEAD</c>: what Dest shares with Source is its own repository, and Source gets every commit of it by Pull.
/// </para>
/// <para>
/// On Source it is the common ancestor of <c>HEAD</c> and <c>refs/remotes/dest/&lt;current branch&gt;</c>: the newest commit of
/// Dest that the working tree is built on. Not the tracking ref itself: a Fetch moves the tracking ref without touching the working
/// tree, and comparing the tree against a newer commit of Dest would offer to send the reverse of Dest's new commits.
/// </para>
/// </remarks>
public sealed class GitSyncBaseLocator : IGitSyncBaseLocator
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitSyncBaseLocator"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="git">The repository service.</param>
    public GitSyncBaseLocator(NodeConfig node, IGitRepositoryService git)
    {
        _node = node;
        _git = git;
    }

    /// <inheritdoc />
    public async Task<GitSyncBaseTarget> LocateAsync(CancellationToken cancellationToken = default)
    {
        var workdir = _node.EffectiveWorkdir;
        var isSource = _node.Role == NodeRole.Source;
        if (workdir is null || !await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return new GitSyncBaseTarget(false, null, null, Texts.Get(isSource ? "SyncBase.Source.NoRepository" : "SyncBase.Dest.NoRepository"));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        if (head is null)
        {
            return new GitSyncBaseTarget(true, null, null, Texts.Get(isSource ? "SyncBase.Source.NoCommits" : "SyncBase.Dest.NoCommits"));
        }

        var branch = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        if (!isSource)
        {
            return new GitSyncBaseTarget(true, branch, head, null);
        }

        if (branch is null)
        {
            return new GitSyncBaseTarget(true, null, null, Texts.Get("SyncBase.Source.Detached"));
        }

        // Runs on every status refresh, so only the two lookups it needs: no divergence counting.
        var tracking = $"refs/remotes/{GitChannelService.RemoteName}/{branch}";
        var destCommit = await _git.ResolveCommitAsync(workdir, tracking, cancellationToken);
        if (destCommit is null)
        {
            return new GitSyncBaseTarget(true, branch, null, Texts.Format("SyncBase.Source.NotFetched", branch));
        }

        var common = await _git.GetMergeBaseAsync(workdir, head, destCommit, cancellationToken);
        return common is null
            ? new GitSyncBaseTarget(true, branch, null, Texts.Format("SyncBase.Source.Unrelated", branch))
            : new GitSyncBaseTarget(true, branch, common, null);
    }
}
