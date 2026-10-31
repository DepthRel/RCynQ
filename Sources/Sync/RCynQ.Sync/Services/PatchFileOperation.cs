using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Patches.Files;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Content;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Services;

/// <summary>
/// Patch files of the user: saves the changes of the working tree as a patch, and brings a patch in or takes it out again.
/// Nothing here reaches the peer; what changes in the working tree shows up as ordinary local changes.
/// </summary>
/// <remarks>
/// Everything runs under the working tree lock. A patch file comes from the user, so it is treated like anything else from
/// outside: Git refuses paths out of the working tree and into <c>.git</c>, and a patch either fits whole or changes nothing.
/// </remarks>
public sealed class PatchFileOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The patch builder that compares a working tree without Git with its snapshot.</summary>
    private readonly IPatchBuilder _builder;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The patch files of a Git working tree.</summary>
    private readonly IGitPatchFileService _files;

    /// <summary>The diff builder of a working tree without Git.</summary>
    private readonly IPatchDiffBuilder _diffs;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFileOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="builder">The patch builder that compares a working tree without Git with its snapshot.</param>
    /// <param name="syncBase">The sync base manager.</param>
    /// <param name="files">The patch files of a Git working tree.</param>
    /// <param name="diffs">The diff builder of a working tree without Git.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="appData">The node data directories.</param>
    public PatchFileOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IPatchBuilder builder,
        SyncBaseManager syncBase,
        IGitPatchFileService files,
        IPatchDiffBuilder diffs,
        IPathUtility pathUtility,
        IGitRepositoryService git,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        IAppDataFactory appData)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _builder = builder;
        _syncBase = syncBase;
        _files = files;
        _diffs = diffs;
        _pathUtility = pathUtility;
        _git = git;
        _tracker = tracker;
        _lock = workingTreeLock;
        _appData = appData;
    }

    /// <summary>
    /// Builds the patch of the changed files: against the Git commit, the files the Synchronization tab lists; in a working
    /// tree without Git, against its snapshot.
    /// </summary>
    /// <param name="paths">The relative paths of the files, or <see langword="null"/> for every changed file. Unknown paths are ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bytes of the patch; empty when there is nothing to save.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree or no sync base, or Git failed.</exception>
    public async Task<byte[]> BuildAsync(IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);

        using (await _lock.AcquireAsync(cancellationToken))
        {
            var syncBase = await _syncBase.EnsureAsync(cancellationToken);
            if (!syncBase.IsReady)
            {
                throw new SyncBridgeException(syncBase.Problem ?? _localizer.Get("Patch.NoBaseline"));
            }

            if (syncBase is { Mode: SyncBaseMode.Git, Commit: { } commit })
            {
                var changed = await _syncBase.ListChangedAsync(Array.Empty<Core.Patches.FileChange>(), syncBase, cancellationToken);
                var wanted = paths is null ? null : new HashSet<string>(paths, SyncBaseManager.PathComparer);
                var files = changed.Select(file => file.Path).Where(path => wanted is null || wanted.Contains(path)).ToArray();
                return await _files.BuildAsync(_paths.Workdir, syncBase.ChangesSince ?? commit, files, cancellationToken);
            }

            var envelope = await _builder.BuildAsync(_node.Id, _node.Id, _paths.SyncBase, _paths.Workdir, cancellationToken);
            var pending = await _syncBase.SelectPendingAsync(envelope.Manifest, syncBase, cancellationToken);
            var selected = await _syncBase.SelectForSendAsync(pending, syncBase, paths, cancellationToken);
            var content = new WorkdirPatchContent(_localizer, _paths.Workdir, _pathUtility);
            return await _diffs.BuildAsync(selected, _paths.SyncBase, content, PatchDiffLimits.Full, cancellationToken);
        }
    }

    /// <summary>
    /// Lists the files a patch changes.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files, in the order of the patch.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree, or the bytes hold no patch or are too many.</exception>
    public async Task<IReadOnlyList<ChangedFile>> ReadAsync(byte[] patch, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        PatchOperations.EnsureParticipant(_node, _localizer);
        EnsureSize(patch);

        var file = await WriteTemporaryAsync(patch, cancellationToken);
        try
        {
            var listed = await _files.ListAsync(_paths.Workdir, file, cancellationToken);
            return listed.Count > 0 ? listed : throw new SyncBridgeException(_localizer.Get("PatchFile.Error.NotAPatch"));
        }
        finally
        {
            DeleteQuietly(file);
        }
    }

    /// <summary>
    /// Brings a patch into the working tree, or takes it out of it. No commit and no ref changes. A Workstation also takes
    /// removed files out of its index; the repository of an Origin stays as it is, so what was staged there stays staged.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <param name="paths">The files of the patch to use, or <see langword="null"/> for all of them.</param>
    /// <param name="reverse">Whether the changes are taken out instead of brought in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended; when it failed, the working tree stayed as it was.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree, or the choice of files or the patch cannot be used.</exception>
    public async Task<PatchFileApplyResult> ApplyAsync(byte[] patch, IReadOnlyCollection<string>? paths, bool reverse, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(patch);
        PatchOperations.EnsureParticipant(_node, _localizer);
        EnsureSize(patch);
        if (paths is { Count: 0 })
        {
            throw new SyncBridgeException(_localizer.Get("PatchFile.NothingChosen"));
        }

        PatchFileApplyResult result;
        var file = await WriteTemporaryAsync(patch, cancellationToken);
        try
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                result = await ApplyLockedAsync(file, paths, reverse, cancellationToken);
            }
        }
        finally
        {
            DeleteQuietly(file);
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.RefreshAsync(CancellationToken.None));
        return result;
    }

    /// <summary>
    /// Applies a patch file. The caller holds the working tree lock.
    /// </summary>
    /// <param name="file">The patch file.</param>
    /// <param name="paths">The files of the patch to use, or <see langword="null"/> for all of them.</param>
    /// <param name="reverse">Whether the changes are taken out instead of brought in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended.</returns>
    /// <exception cref="SyncBridgeException">A chosen file is not in the patch.</exception>
    private async Task<PatchFileApplyResult> ApplyLockedAsync(string file, IReadOnlyCollection<string>? paths, bool reverse, CancellationToken cancellationToken)
    {
        var listed = (await _files.ListAsync(_paths.Workdir, file, cancellationToken)).Select(entry => entry.Path).ToArray();
        if (listed.Length == 0)
        {
            return new PatchFileApplyResult(false, _localizer.Get("PatchFile.Error.NotAPatch"), string.Empty);
        }

        var known = new HashSet<string>(listed, SyncBaseManager.PathComparer);
        if (paths?.FirstOrDefault(path => !known.Contains(path)) is { } unknown)
        {
            throw new SyncBridgeException(_localizer.Format("PatchFile.NotInPatch", unknown));
        }

        var wanted = paths is null ? known : new HashSet<string>(paths, SyncBaseManager.PathComparer);
        var chosen = listed.Where(wanted.Contains).ToArray();
        var skipped = listed.Where(path => !wanted.Contains(path)).ToArray();

        var result = await _files.ApplyAsync(_paths.Workdir, new GitPatchApplyRequest(file, reverse, chosen, skipped), cancellationToken);
        if (result.Succeeded && reverse && _node.Role == NodeRole.Workstation && await _git.IsWorkTreeAsync(_paths.Workdir, cancellationToken))
        {
            // The working tree no longer has the changes, and the next commit must not bring them back from the index.
            await _files.UnstageAsync(_paths.Workdir, chosen, cancellationToken);
        }

        return new PatchFileApplyResult(result.Succeeded, Describe(result, chosen.Length, reverse), result.Output);
    }

    /// <summary>
    /// Words how applying a patch ended.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="fileCount">The number of files that were to change.</param>
    /// <param name="reverse">Whether the patch was taken out.</param>
    /// <returns>The message.</returns>
    private string Describe(GitPatchApplyResult result, int fileCount, bool reverse)
    {
        if (result.Succeeded)
        {
            return _localizer.Format(reverse ? "PatchFile.Removed" : "PatchFile.Applied", fileCount);
        }

        var files = FileListText.Describe(_localizer, result.Files);
        return result.Failure switch
        {
            PatchFileFailure.NotAPatch => _localizer.Get("PatchFile.Error.NotAPatch"),
            PatchFileFailure.UnsafePath => _localizer.Format("PatchFile.Error.UnsafePath", files),
            PatchFileFailure.DoesNotApply => _localizer.Format("PatchFile.Error.DoesNotApply", files),
            PatchFileFailure.AlreadyExists => _localizer.Format("PatchFile.Error.AlreadyExists", files),
            PatchFileFailure.MissingFile => _localizer.Format("PatchFile.Error.MissingFile", files),
            PatchFileFailure.TooManyFiles => _localizer.Get("PatchFile.Error.TooManyFiles"),
            _ => _localizer.Get("PatchFile.Error.Other")
        };
    }

    /// <summary>
    /// Refuses a patch above the limit.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <exception cref="SyncBridgeException">The patch is too large.</exception>
    private void EnsureSize(byte[] patch)
    {
        if (patch.LongLength > ApiLimits.MAX_PATCH_FILE_BYTES)
        {
            throw new SyncBridgeException(_localizer.Format("PatchFile.TooLarge", ApiLimits.MAX_PATCH_FILE_BYTES / (1024 * 1024)));
        }
    }

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

        var file = Path.Combine(folder, $"patch-file-{Guid.NewGuid():N}.patch");
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
