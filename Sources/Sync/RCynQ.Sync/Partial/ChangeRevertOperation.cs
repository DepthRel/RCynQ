using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Partial;

/// <summary>
/// Takes back the changes of one file of the working tree of a Workstation that are not staged, when the user asks.
/// </summary>
/// <remarks>
/// Only a Workstation does it: the repository of an Origin changes only by the patches the user sent or applied there. The
/// file is restored from the Git index, so what the user staged stays; a file Git lists as untracked is deleted, and any
/// other path is refused. A choice of the changes of the file that goes in part is forgotten with them.
/// </remarks>
public sealed class ChangeRevertOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The stored choices of the changes that go.</summary>
    private readonly PartialChangeStore _store;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeRevertOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="store">The stored choices of the changes that go.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="tracker">The state tracker.</param>
    public ChangeRevertOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IPathUtility pathUtility,
        IGitRepositoryService git,
        PartialChangeStore store,
        WorkingTreeLock workingTreeLock,
        StateTracker tracker)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _pathUtility = pathUtility ?? throw new ArgumentNullException(nameof(pathUtility));
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _lock = workingTreeLock ?? throw new ArgumentNullException(nameof(workingTreeLock));
        _tracker = tracker ?? throw new ArgumentNullException(nameof(tracker));
    }

    /// <summary>
    /// Takes back the changes of a file that are not staged.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Whether the file was restored, deleted or had nothing to take back.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    /// <exception cref="SyncBridgeException">
    /// The node is not a Workstation, its working tree has no Git, Git does not know the file, or git failed.
    /// </exception>
    public async Task<RevertChangesResult> ExecuteAsync(RevertChangesRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (_node.Role != NodeRole.Workstation)
        {
            throw new SyncBridgeException(_localizer.Format("Changes.RevertOnlyWorkstation", _node.Role));
        }

        var workdir = _paths.Workdir;
        var path = ChangePath.Normalize(_pathUtility, workdir, request.Path);

        WorkingTreeRevertOutcome outcome;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
            {
                throw new SyncBridgeException(_localizer.Get("Changes.RevertNeedsGit"));
            }

            outcome = await _git.RevertWorkingTreeFileAsync(workdir, path, cancellationToken);
            if (outcome == WorkingTreeRevertOutcome.Unknown)
            {
                throw new SyncBridgeException(_localizer.Format("Changes.CannotRevert", path));
            }

            if (outcome != WorkingTreeRevertOutcome.NothingToRevert)
            {
                // The choice was made on changes that are gone now.
                await _store.RemoveAsync(path, cancellationToken);
            }
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(CancellationToken.None));
        return new RevertChangesResult(path, outcome);
    }
}
