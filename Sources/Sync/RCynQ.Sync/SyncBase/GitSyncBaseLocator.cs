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
/// On Origin it is <c>HEAD</c>: what Origin shares with Workstation is its own repository, and Workstation gets every commit of it by Pull.
/// </para>
/// <para>
/// On Workstation it is the common ancestor of <c>HEAD</c> and <c>refs/remotes/rcynq/&lt;current branch&gt;</c>: the newest commit of
/// Origin that the working tree is built on. Not the tracking ref itself: a Fetch moves the tracking ref without touching the working
/// tree, and comparing the tree against a newer commit of Origin would offer to send the reverse of Origin's new commits.
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

    /// <summary>Renames the remote-tracking namespace of an earlier version, or <see langword="null"/>.</summary>
    private readonly LegacyRemoteMigration? _legacyRemote;

    /// <summary>Tells which repository the node works with now.</summary>
    private readonly IActiveRepository _repository;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitSyncBaseLocator"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="legacyRemote">Renames the remote-tracking namespace of an earlier version; <see langword="null"/> leaves it alone.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> for the one the node started with.</param>
    public GitSyncBaseLocator(ILocalizer localizer, NodeConfig node, IGitRepositoryService git, LegacyRemoteMigration? legacyRemote = null, IActiveRepository? repository = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _git = git;
        _legacyRemote = legacyRemote;
        _repository = repository ?? FixedRepository.FromNode(node);
    }

    /// <inheritdoc />
    public async Task<GitSyncBaseTarget> LocateAsync(CancellationToken cancellationToken = default)
    {
        var workdir = _repository.Current?.Path;
        var isWorkstation = _node.Role == NodeRole.Workstation;
        if (workdir is null || !await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return new GitSyncBaseTarget(false, null, null, _localizer.Get(isWorkstation ? "SyncBase.Workstation.NoRepository" : "SyncBase.Origin.NoRepository"));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        if (head is null)
        {
            return new GitSyncBaseTarget(true, null, null, _localizer.Get(isWorkstation ? "SyncBase.Workstation.NoCommits" : "SyncBase.Origin.NoCommits"));
        }

        var branch = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        if (!isWorkstation)
        {
            return new GitSyncBaseTarget(true, branch, head, null);
        }

        if (branch is null)
        {
            return new GitSyncBaseTarget(true, null, null, _localizer.Get("SyncBase.Workstation.Detached"));
        }

        if (_legacyRemote is not null)
        {
            await _legacyRemote.EnsureAsync(workdir, cancellationToken);
        }

        // Runs on every status refresh, so only the two lookups it needs: no divergence counting.
        var tracking = $"refs/remotes/{GitChannelService.REMOTE_NAME}/{branch}";
        var originCommit = await _git.ResolveCommitAsync(workdir, tracking, cancellationToken);
        if (originCommit is null)
        {
            return new GitSyncBaseTarget(true, branch, null, _localizer.Format("SyncBase.Workstation.NotFetched", branch));
        }

        var common = await _git.GetMergeBaseAsync(workdir, head, originCommit, cancellationToken);
        return common is null
            ? new GitSyncBaseTarget(true, branch, null, _localizer.Format("SyncBase.Workstation.Unrelated", branch))
            : new GitSyncBaseTarget(true, branch, common, null);
    }
}
