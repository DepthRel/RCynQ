using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Git.Patches.Files;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;
using RCynQ.Sync.State;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Switches the working tree of a Workstation to another branch: a local one, or one fetched from the Origin.
/// </summary>
/// <remarks>
/// <para>
/// Only the working copy of the Workstation changes; the Origin is not asked and not touched. The sync base follows the
/// new branch on its own, since it is located from the current branch.
/// </para>
/// <para>
/// Nothing the user typed is lost: a working tree with changes is switched only when the request says what happens to
/// them (carried over by Git, or taken out after the user saved them to a patch file), and a detached <c>HEAD</c> whose
/// commits no branch holds is never left.
/// </para>
/// </remarks>
public sealed class BranchSwitchService
{
    /// <summary>The most commits of a detached <c>HEAD</c> named in a refusal.</summary>
    private const int MAX_LISTED_COMMITS = 10;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The branches of the working copy.</summary>
    private readonly IGitBranchService _branches;

    /// <summary>The patch files of the working tree.</summary>
    private readonly IGitPatchFileService _files;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The state tracker refreshed after a switch, or <see langword="null"/>.</summary>
    private readonly StateTracker? _tracker;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="BranchSwitchService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="branches">The branches of the working copy.</param>
    /// <param name="files">The patch files of the working tree.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="tracker">The state tracker refreshed after a switch; <see langword="null"/> refreshes nothing.</param>
    public BranchSwitchService(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IGitRepositoryService git,
        IGitBranchService branches,
        IGitPatchFileService files,
        WorkingTreeLock workingTreeLock,
        IAppDataFactory appData,
        StateTracker? tracker = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _git = git ?? throw new ArgumentNullException(nameof(git));
        _branches = branches ?? throw new ArgumentNullException(nameof(branches));
        _files = files ?? throw new ArgumentNullException(nameof(files));
        _lock = workingTreeLock ?? throw new ArgumentNullException(nameof(workingTreeLock));
        _appData = appData ?? throw new ArgumentNullException(nameof(appData));
        _tracker = tracker;
    }

    /// <summary>
    /// Lists the branches the working tree can be switched to.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The current branch, the branch of the Origin and the branches; no branches for a folder without Git.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Workstation.</exception>
    public async Task<GitSwitchBranchesResponse> ListAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkstation();

        var workdir = _paths.Workdir;
        if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return new GitSwitchBranchesResponse(null, null, Array.Empty<Core.State.GitSwitchableBranch>());
        }

        var current = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        var originHead = await _branches.GetRemoteHeadAsync(workdir, GitChannelService.REMOTE_NAME, cancellationToken);
        var branches = await _branches.ListSwitchableAsync(workdir, GitChannelService.REMOTE_NAME, cancellationToken);
        return new GitSwitchBranchesResponse(current, originHead, branches);
    }

    /// <summary>
    /// Builds the patch of every uncommitted change of the working tree against <c>HEAD</c>: what a switch would take out
    /// when the user saves the changes first. Nothing changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bytes of the patch; empty when nothing is changed.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Workstation, or Git failed.</exception>
    public async Task<byte[]> BuildChangesPatchAsync(CancellationToken cancellationToken = default)
    {
        EnsureWorkstation();

        var workdir = _paths.Workdir;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
            if (head is null)
            {
                return Array.Empty<byte>();
            }

            var changed = await _git.GetChangedSinceAsync(workdir, head, cancellationToken);
            return await _files.BuildAsync(workdir, head, changed, cancellationToken);
        }
    }

    /// <summary>
    /// Switches the working tree to a branch.
    /// </summary>
    /// <param name="request">The branch and what happens to uncommitted changes.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended; unless the outcome is <see cref="BranchSwitchOutcome.Switched"/>, the working tree is as it was.</returns>
    /// <exception cref="SyncBridgeException">The node is not a Workstation.</exception>
    public async Task<SwitchBranchResult> SwitchAsync(SwitchBranchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        EnsureWorkstation();

        var workdir = _paths.Workdir;
        if (!GitNames.IsBranch(request.Branch))
        {
            return Refused(_localizer.Format("BranchSwitch.InvalidName", request.Branch ?? string.Empty));
        }

        if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return Refused(_localizer.Get("BranchSwitch.NoRepository"));
        }

        if (request.Mode == BranchSwitchMode.SavePatch && request.Patch is not { Length: > 0 })
        {
            return Refused(_localizer.Get("BranchSwitch.PatchMissing"));
        }

        if (request.Patch is { LongLength: > ApiLimits.MAX_PATCH_FILE_BYTES })
        {
            return Refused(_localizer.Format("PatchFile.TooLarge", ApiLimits.MAX_PATCH_FILE_BYTES / (1024 * 1024)));
        }

        SwitchBranchResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            result = await SwitchLockedAsync(workdir, request, cancellationToken);
        }

        if (result.Outcome == BranchSwitchOutcome.Switched && _tracker is not null)
        {
            await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(CancellationToken.None));
        }

        return result with { Branch = await _git.GetBranchInfoAsync(workdir, GitChannelService.REMOTE_NAME, cancellationToken) };
    }

    /// <summary>
    /// Switches the working tree; the caller holds the working tree lock.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended.</returns>
    private async Task<SwitchBranchResult> SwitchLockedAsync(string workdir, SwitchBranchRequest request, CancellationToken cancellationToken)
    {
        var branch = request.Branch;
        var current = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        if (current == branch)
        {
            return Switched(_localizer.Format("BranchSwitch.AlreadyCurrent", branch));
        }

        var known = await _branches.ListSwitchableAsync(workdir, GitChannelService.REMOTE_NAME, cancellationToken);
        if (!known.Any(entry => entry.Name == branch))
        {
            return Refused(_localizer.Format("BranchSwitch.NoSuchBranch", branch));
        }

        if (current is null && await _branches.GetCommitsOnlyInHeadAsync(workdir, MAX_LISTED_COMMITS, cancellationToken) is { Count: > 0 } orphans)
        {
            var listed = string.Join(", ", orphans.Select(commit => commit[..10]));
            return Refused(_localizer.Format("BranchSwitch.DetachedCommits", listed));
        }

        // Only an explicit choice lets changes stay in the way: a mode the service does not know counts as "only if clean".
        var dirty = await _git.GetDirtyPathsAsync(workdir, cancellationToken);
        if (dirty.Count > 0 && request.Mode is not (BranchSwitchMode.CarryChanges or BranchSwitchMode.SavePatch))
        {
            return new SwitchBranchResult(BranchSwitchOutcome.ChangesInTheWay, _localizer.Format("BranchSwitch.ChangesInTheWay", branch), dirty, null);
        }

        var left = current is null ? 0 : await _branches.CountCommitsNotOnRemoteAsync(workdir, GitChannelService.REMOTE_NAME, cancellationToken);
        var takeOut = request.Mode == BranchSwitchMode.SavePatch && dirty.Count > 0;
        var patchFile = takeOut ? await WriteTemporaryAsync(request.Patch!, cancellationToken) : null;
        try
        {
            IReadOnlyList<string> patched = Array.Empty<string>();
            if (patchFile is not null)
            {
                patched = (await _files.ListAsync(workdir, patchFile, cancellationToken)).Select(file => file.Path).ToArray();
                if (patched.Count == 0)
                {
                    return Refused(_localizer.Get("PatchFile.Error.NotAPatch"));
                }

                var removed = await _files.ApplyAsync(workdir, new GitPatchApplyRequest(patchFile, true, patched, Array.Empty<string>()), cancellationToken);
                if (!removed.Succeeded)
                {
                    return Refused(_localizer.Format("BranchSwitch.PatchNotRemoved", branch, removed.Output));
                }

                // The working tree no longer has the changes, and the next commit must not bring them back from the index.
                await _files.UnstageAsync(workdir, patched, cancellationToken);
            }

            var attempt = await _branches.SwitchAsync(workdir, GitChannelService.REMOTE_NAME, branch, cancellationToken);
            if (!attempt.Succeeded)
            {
                return await RefuseAttemptAsync(workdir, branch, attempt, patchFile, patched, cancellationToken);
            }

            return Switched(await DescribeSwitchAsync(workdir, branch, current, attempt.Created, left, dirty.Count > 0 ? request.Mode : null, cancellationToken));
        }
        finally
        {
            if (patchFile is not null)
            {
                DeleteQuietly(patchFile);
            }
        }
    }

    /// <summary>
    /// Words a switch Git refused, after putting saved changes that were taken out back into the working tree.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="branch">The branch asked for.</param>
    /// <param name="attempt">The refusal of Git.</param>
    /// <param name="patchFile">The patch that was taken out, or <see langword="null"/>.</param>
    /// <param name="patched">The files of that patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The refusal.</returns>
    private async Task<SwitchBranchResult> RefuseAttemptAsync(string workdir, string branch, GitSwitchAttempt attempt, string? patchFile, IReadOnlyList<string> patched, CancellationToken cancellationToken)
    {
        var reason = attempt.BlockingFiles.Count > 0
            ? _localizer.Format("BranchSwitch.CarryRefused", branch, FileListText.Describe(_localizer, attempt.BlockingFiles))
            : _localizer.Format("BranchSwitch.Failed", branch, attempt.Output);
        if (patchFile is null)
        {
            return Refused(reason, attempt.BlockingFiles);
        }

        var restored = await _files.ApplyAsync(workdir, new GitPatchApplyRequest(patchFile, false, patched, Array.Empty<string>()), CancellationToken.None);
        var tail = _localizer.Get(restored.Succeeded ? "BranchSwitch.ChangesRestored" : "BranchSwitch.ChangesNotRestored");
        return Refused($"{reason}\n{tail}", attempt.BlockingFiles);
    }

    /// <summary>
    /// Words a switch that succeeded, and makes a branch created from the Origin follow it.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="branch">The new branch.</param>
    /// <param name="previous">The branch left, or <see langword="null"/> for a detached <c>HEAD</c>.</param>
    /// <param name="created">Whether the local branch was created from the Origin.</param>
    /// <param name="left">The commits of the branch left that the Origin does not have.</param>
    /// <param name="changes">What happened to uncommitted changes, or <see langword="null"/> when there were none.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The message.</returns>
    private async Task<string> DescribeSwitchAsync(string workdir, string branch, string? previous, bool created, int left, BranchSwitchMode? changes, CancellationToken cancellationToken)
    {
        var lines = new List<string> { _localizer.Format("BranchSwitch.Switched", branch) };
        if (created)
        {
            await _git.TrackRemoteBranchAsync(workdir, GitChannelService.REMOTE_NAME, branch, cancellationToken);
            lines.Add(_localizer.Format("BranchSwitch.Created", branch, GitChannelService.REMOTE_NAME));
        }

        if (changes == BranchSwitchMode.CarryChanges)
        {
            lines.Add(_localizer.Get("BranchSwitch.ChangesCarried"));
        }
        else if (changes == BranchSwitchMode.SavePatch)
        {
            lines.Add(_localizer.Get("BranchSwitch.ChangesTakenOut"));
        }

        if (previous is not null && left > 0)
        {
            lines.Add(_localizer.Format("BranchSwitch.LeftCommits", previous, left));
        }

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Refuses a request of a node that is not a Workstation: only a Workstation keeps branches of the Origin and may switch.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is not a Workstation.</exception>
    private void EnsureWorkstation()
    {
        if (_node.Role != NodeRole.Workstation)
        {
            throw new SyncBridgeException(_localizer.Format("BranchSwitch.NotWorkstation", _node.Role));
        }
    }

    /// <summary>
    /// Builds a result of a switch that happened.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    private static SwitchBranchResult Switched(string message)
        => new(BranchSwitchOutcome.Switched, message, Array.Empty<string>(), null);

    /// <summary>
    /// Builds a result of a refused switch.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="files">The files in the way, if any.</param>
    /// <returns>The result.</returns>
    private static SwitchBranchResult Refused(string message, IReadOnlyList<string>? files = null)
        => new(BranchSwitchOutcome.Refused, message, files ?? Array.Empty<string>(), null);

    /// <summary>
    /// Stores a patch for Git in the temporary folder of the node.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The full path of the file.</returns>
    private async Task<string> WriteTemporaryAsync(byte[] patch, CancellationToken cancellationToken)
    {
        var folder = _appData.Create().TempDirectory;
        Directory.CreateDirectory(folder);

        var file = Path.Combine(folder, $"switch-{Guid.NewGuid():N}.patch");
        await File.WriteAllBytesAsync(file, patch, cancellationToken);
        return file;
    }

    /// <summary>
    /// Deletes a temporary file, ignoring failures.
    /// </summary>
    /// <param name="file">The file.</param>
    private static void DeleteQuietly(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data; the cleanup of the node removes it later.
        }
    }
}
