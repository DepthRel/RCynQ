using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Sync.SyncBase;

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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitSyncBaseLocator"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="git">The repository service.</param>
    public GitSyncBaseLocator(ILocalizer localizer, NodeConfig node, IGitRepositoryService git)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
            return new GitSyncBaseTarget(false, null, null, _localizer.Get(isSource ? "SyncBase.Source.NoRepository" : "SyncBase.Dest.NoRepository"));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        if (head is null)
        {
            return new GitSyncBaseTarget(true, null, null, _localizer.Get(isSource ? "SyncBase.Source.NoCommits" : "SyncBase.Dest.NoCommits"));
        }

        var branch = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        if (!isSource)
        {
            return new GitSyncBaseTarget(true, branch, head, null);
        }

        if (branch is null)
        {
            return new GitSyncBaseTarget(true, null, null, _localizer.Get("SyncBase.Source.Detached"));
        }

        // Runs on every status refresh, so only the two lookups it needs: no divergence counting.
        var tracking = $"refs/remotes/{GitChannelService.RemoteName}/{branch}";
        var destCommit = await _git.ResolveCommitAsync(workdir, tracking, cancellationToken);
        if (destCommit is null)
        {
            return new GitSyncBaseTarget(true, branch, null, _localizer.Format("SyncBase.Source.NotFetched", branch));
        }

        var common = await _git.GetMergeBaseAsync(workdir, head, destCommit, cancellationToken);
        return common is null
            ? new GitSyncBaseTarget(true, branch, null, _localizer.Format("SyncBase.Source.Unrelated", branch))
            : new GitSyncBaseTarget(true, branch, common, null);
    }
}
