using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Commits;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Finds the commits of the current branch of a Workstation that the Origin does not have yet.
/// </summary>
/// <remarks>
/// They are the commits on the first-parent chain from <c>HEAD</c> down to the branch of the Origin
/// (<c>refs/remotes/rcynq/&lt;branch&gt;</c>), and only when that chain starts right on it: then the Origin can take them
/// as a fast-forward. A branch the Origin does not have, a branch that diverged from it or a detached <c>HEAD</c> lists no
/// commits and says why.
/// </remarks>
public sealed class UnsentCommitsReader
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The Git operations of commits.</summary>
    private readonly IGitCommitService _commits;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="UnsentCommitsReader"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="commits">The Git operations of commits.</param>
    /// <param name="patches">The patch repository.</param>
    public UnsentCommitsReader(ILocalizer localizer, NodeConfig node, SyncPaths paths, IGitRepositoryService git, IGitCommitService commits, IPatchRepository patches)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _git = git;
        _commits = commits;
        _patches = patches;
    }

    /// <summary>
    /// Lists the unsent commits of the current branch.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commits, oldest first, or the reason there are none to send.</returns>
    public async Task<UnsentCommitsResponse> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (_node.Role != NodeRole.Workstation)
        {
            return Problem(UnsentCommitsState.NotWorkstation, null, null, null, _localizer.Format("Commits.OnlyWorkstation", _node.Role));
        }

        var workdir = _paths.Repository?.Path;
        if (workdir is null || !await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return Problem(UnsentCommitsState.NoRepository, null, null, null, _localizer.Get("Commits.NoRepository"));
        }

        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        var branch = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        if (head is null || branch is null || !GitNames.IsBranch(branch))
        {
            return Problem(UnsentCommitsState.Detached, branch, null, head, _localizer.Get("Commits.Detached"));
        }

        var origin = await _git.ResolveCommitAsync(workdir, $"refs/remotes/{GitChannelService.REMOTE_NAME}/{branch}", cancellationToken);
        if (origin is null)
        {
            return Problem(UnsentCommitsState.NoOriginBranch, branch, null, head, _localizer.Format("Commits.NoOriginBranch", branch));
        }

        if (origin == head)
        {
            return new UnsentCommitsResponse(UnsentCommitsState.Ready, branch, origin, head, Array.Empty<CommitInfo>(), Array.Empty<string>(), null);
        }

        // The first commit must sit right on the commit of the Origin, or the Origin cannot fast-forward to the commits.
        var chain = await _commits.GetFirstParentChainAsync(workdir, origin, head, null, cancellationToken);
        if (chain.Count == 0 || chain[0].FirstParent != origin)
        {
            return Problem(UnsentCommitsState.Diverged, branch, origin, head, _localizer.Format("Commits.Diverged", branch));
        }

        if (chain.Count > ApiLimits.MAX_DELIVERY_COMMITS)
        {
            return Problem(UnsentCommitsState.TooMany, branch, origin, head, _localizer.Format("Commits.TooMany", chain.Count, ApiLimits.MAX_DELIVERY_COMMITS));
        }

        var commits = await _commits.ListFirstParentAsync(workdir, origin, head, ApiLimits.MAX_DELIVERY_COMMITS, cancellationToken);
        return new UnsentCommitsResponse(UnsentCommitsState.Ready, branch, origin, head, commits, await WaitingAsync(branch, commits, cancellationToken), null);
    }

    /// <summary>
    /// Finds the unsent commits that a delivery is waiting with on the Origin.
    /// </summary>
    /// <param name="branch">The branch.</param>
    /// <param name="commits">The unsent commits.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Their hashes.</returns>
    private async Task<IReadOnlyList<string>> WaitingAsync(string branch, IReadOnlyList<CommitInfo> commits, CancellationToken cancellationToken)
    {
        var repository = _paths.Repository;
        var waiting = (await _patches.GetByStatusAsync(PatchDirection.Outgoing, PatchStatus.Waiting, cancellationToken))
            .Where(patch => patch.Commits?.Branch == branch && (repository is not { IsBound: true } || repository.IsNamed(patch.Repository)))
            .SelectMany(patch => patch.Commits!.Commits.Select(commit => commit.Hash))
            .ToHashSet(StringComparer.Ordinal);
        return commits.Select(commit => commit.Hash).Where(waiting.Contains).ToArray();
    }

    /// <summary>
    /// Builds an answer that lists no commits.
    /// </summary>
    /// <param name="state">Why there are no commits to send.</param>
    /// <param name="branch">The branch, or <see langword="null"/>.</param>
    /// <param name="origin">The commit of the Origin, or <see langword="null"/>.</param>
    /// <param name="head">The commit checked out, or <see langword="null"/>.</param>
    /// <param name="problem">The reason, worded for the user.</param>
    /// <returns>The answer.</returns>
    private static UnsentCommitsResponse Problem(UnsentCommitsState state, string? branch, string? origin, string? head, string problem)
        => new(state, branch, origin, head, Array.Empty<CommitInfo>(), Array.Empty<string>(), problem);
}
