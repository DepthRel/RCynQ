using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Commits;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Rolls back unsent commits of a Workstation, as <c>git reset</c> does.
/// </summary>
/// <remarks>
/// Only the commits the Origin does not have are rolled back, and only while the working tree has no change against
/// <c>HEAD</c>: then nothing the user has not committed can be lost or mixed with the changes of the dropped commits. The
/// rollback is refused as well when <c>HEAD</c> moved since the user chose the commit. The old <c>HEAD</c> stays in the reflog.
/// </remarks>
public sealed class CommitResetOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>Finds the unsent commits.</summary>
    private readonly UnsentCommitsReader _reader;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The Git operations of commits.</summary>
    private readonly IGitCommitService _commits;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitResetOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="reader">Finds the unsent commits.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="commits">The Git operations of commits.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="tracker">The state tracker.</param>
    public CommitResetOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        UnsentCommitsReader reader,
        IGitRepositoryService git,
        IGitCommitService commits,
        WorkingTreeLock workingTreeLock,
        StateTracker tracker)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _reader = reader;
        _git = git;
        _commits = commits;
        _lock = workingTreeLock;
        _tracker = tracker;
    }

    /// <summary>
    /// Rolls the current branch back to a commit.
    /// </summary>
    /// <param name="target">The commit that becomes <c>HEAD</c>: an unsent commit, or the commit the Origin has.</param>
    /// <param name="mode">What happens to the changes of the dropped commits.</param>
    /// <param name="expectedHead">The commit checked out when the user chose.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome; a refusal is reported in the result and changes nothing.</returns>
    public async Task<GitOperationResult> ExecuteAsync(string target, CommitResetMode mode, string expectedHead, CancellationToken cancellationToken = default)
    {
        if (_node.Role != NodeRole.Workstation)
        {
            return Refused(_localizer.Format("Commits.OnlyWorkstation", _node.Role));
        }

        if (!Enum.IsDefined(mode))
        {
            return Refused(_localizer.Format("Commits.UnknownMode", mode));
        }

        string message;
        try
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                var unsent = await _reader.ReadAsync(cancellationToken);
                if (unsent.Problem is not null)
                {
                    return Refused(unsent.Problem);
                }

                if (!string.Equals(unsent.HeadCommit, expectedHead, StringComparison.OrdinalIgnoreCase))
                {
                    return Refused(_localizer.Get("Commits.HeadChanged"));
                }

                var hashes = new[] { unsent.OriginCommit! }.Concat(unsent.Commits.Select(commit => commit.Hash)).ToList();
                var index = hashes.FindIndex(hash => string.Equals(hash, target, StringComparison.OrdinalIgnoreCase));
                if (index < 0 || index == hashes.Count - 1)
                {
                    return Refused(_localizer.Format("Commits.NotRollbackTarget", target));
                }

                var dirty = await _git.GetDirtyPathsAsync(_paths.Workdir, cancellationToken);
                if (dirty.Count > 0)
                {
                    return Refused(_localizer.Format("Commits.ResetDirty", FileListText.Describe(_localizer, dirty)));
                }

                await _commits.ResetAsync(_paths.Workdir, hashes[index], mode, cancellationToken);
                message = _localizer.Format("Commits.RolledBack", hashes.Count - 1 - index, hashes[index][..10], mode);
            }
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Refused(_localizer.Describe(ex));
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(CancellationToken.None));
        return new GitOperationResult(true, message, await _git.GetBranchInfoAsync(_paths.Workdir, GitChannelService.REMOTE_NAME, cancellationToken));
    }

    /// <summary>
    /// Builds the result of a refused rollback.
    /// </summary>
    /// <param name="message">Why it was refused.</param>
    /// <returns>The result.</returns>
    private static GitOperationResult Refused(string message) => new(false, message, null);
}
