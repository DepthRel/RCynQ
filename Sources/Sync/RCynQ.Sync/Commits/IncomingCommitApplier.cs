using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Commits;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Paths;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Takes a delivery of commits on an Origin: checks it, and fast-forwards the checked out branch to it when the working
/// tree allows.
/// </summary>
/// <remarks>
/// <para>
/// The commits are taken only when the user allowed it (<see cref="CommitAcceptance"/>), only for the branch checked out
/// on this node and only when its <c>HEAD</c> is the commit they are built on. The objects of the bundle are unpacked into
/// a quarantine beside the repository; nothing reaches <c>.git</c> before the bundle holds exactly the signed commits and
/// every path they touch is a safe one.
/// </para>
/// <para>
/// A working tree with any change against <c>HEAD</c> makes the delivery wait: its bundle stays with the node, and the
/// user applies it once the tree is clean, or declines it. When the fast-forward fails after the packs were moved into
/// the repository, they are taken out again, so a failed delivery leaves <c>.git</c> as it was. The caller holds the
/// working tree lock and records the outcome.
/// </para>
/// </remarks>
public sealed class IncomingCommitApplier
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>Tells whether commits are taken.</summary>
    private readonly CommitAcceptance _acceptance;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The Git operations of commits.</summary>
    private readonly IGitCommitService _commits;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="IncomingCommitApplier"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="acceptance">Tells whether commits are taken.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="commits">The Git operations of commits.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="appData">The node data directories.</param>
    /// <param name="syncBase">The sync base manager.</param>
    public IncomingCommitApplier(
        ILocalizer localizer,
        NodeConfig node,
        CommitAcceptance acceptance,
        SyncPaths paths,
        IGitRepositoryService git,
        IGitCommitService commits,
        IPathUtility pathUtility,
        IPatchFileStorage storage,
        IPatchRepository patches,
        IAppDataFactory appData,
        SyncBaseManager syncBase)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _acceptance = acceptance;
        _paths = paths;
        _git = git;
        _commits = commits;
        _pathUtility = pathUtility;
        _storage = storage;
        _patches = patches;
        _appData = appData;
        _syncBase = syncBase;
    }

    /// <summary>
    /// Checks that a delivery of commits may be received at all, before anything of it is stored.
    /// </summary>
    /// <param name="commits">The commits.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the delivery may be received.</returns>
    /// <exception cref="PatchRejectedException">
    /// The node is not an Origin, does not take commits, has commits waiting, or is not at the commit they are built on.
    /// </exception>
    public async Task EnsureAdmissibleAsync(CommitDelivery commits, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(commits);
        if (_node.Role != NodeRole.Origin)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, _localizer.Format("Commits.OnlyOrigin", _node.Role));
        }

        if (!_acceptance.IsEnabled)
        {
            throw new PatchRejectedException(PatchRejectionReason.CommitsNotAccepted, _localizer.Get("Commits.NotAccepted"));
        }

        if (await HasWaitingAsync(cancellationToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.CommitsWaiting, _localizer.Get("Commits.AlreadyWaiting"));
        }

        if (await FindMismatchAsync(commits, cancellationToken) is { } mismatch)
        {
            throw new PatchRejectedException(PatchRejectionReason.VersionMismatch, LocalizedText.Render(mismatch, _localizer)!);
        }
    }

    /// <summary>
    /// Refuses a patch of files while commits wait to be applied: the patch would change the working tree they wait for.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when patches may be received.</returns>
    /// <exception cref="PatchRejectedException">Commits wait to be applied.</exception>
    public async Task EnsureNoWaitingAsync(CancellationToken cancellationToken = default)
    {
        if (await HasWaitingAsync(cancellationToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.CommitsWaiting, _localizer.Get("Commits.PatchesRefused"));
        }
    }

    /// <summary>
    /// Checks whether a delivery of commits waits to be applied to the repository the node works with.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when one waits.</returns>
    public async Task<bool> HasWaitingAsync(CancellationToken cancellationToken = default)
    {
        var repository = _paths.Repository;
        return (await _patches.GetByStatusAsync(PatchDirection.Incoming, PatchStatus.Waiting, cancellationToken))
            .Any(patch => patch.Commits is not null && (repository is not { IsBound: true } || repository.IsNamed(patch.Repository)));
    }

    /// <summary>
    /// Applies a delivery whose bundle has arrived in full, or tells why it waits or was refused.
    /// </summary>
    /// <param name="envelope">The delivery.</param>
    /// <param name="retry">
    /// <see langword="true"/> when the user applies a delivery that waits: a branch or <c>HEAD</c> that differs then keeps
    /// it waiting instead of refusing it, and the setting that takes commits is not asked again.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Applied, waiting, refused or failed; a failure is part of the result, not an exception.</returns>
    /// <exception cref="ArgumentException">The envelope delivers no commits.</exception>
    public async Task<PatchApplyResult> ApplyAsync(PatchEnvelope envelope, bool retry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var commits = envelope.Commits ?? throw new ArgumentException("The envelope delivers no commits.", nameof(envelope));
        if (!retry && !_acceptance.IsEnabled)
        {
            return PatchApplyResult.Rejected(envelope.Id, LocalizedText.Encode("Commits.NotAccepted"));
        }

        if (await FindMismatchAsync(commits, cancellationToken) is { } mismatch)
        {
            return retry ? PatchApplyResult.Waiting(envelope.Id, mismatch) : PatchApplyResult.Rejected(envelope.Id, mismatch);
        }

        var temp = Path.Combine(_appData.Create().TempDirectory, $"commits-{envelope.Id:N}");
        try
        {
            Directory.CreateDirectory(temp);
            await _storage.StageContentAsync(envelope.Id, envelope.ContentHashes, cancellationToken);
            var bundle = Path.Combine(temp, "commits.bundle");
            await CopyBundleAsync(envelope.Id, commits.BundleSha256, bundle, cancellationToken);

            GitQuarantine quarantine;
            try
            {
                quarantine = await _commits.UnbundleAsync(_paths.Workdir, bundle, Path.Combine(temp, "objects"), cancellationToken);
            }
            catch (GitExecutionException ex)
            {
                return PatchApplyResult.Rejected(envelope.Id, LocalizedText.Encode("Commits.BundleInvalid", ex.Message));
            }

            if (await FindContentProblemAsync(commits, quarantine, cancellationToken) is { } problem)
            {
                return PatchApplyResult.Rejected(envelope.Id, problem);
            }

            var dirty = await _git.GetDirtyPathsAsync(_paths.Workdir, cancellationToken);
            if (dirty.Count > 0)
            {
                return PatchApplyResult.Waiting(envelope.Id, LocalizedText.Encode("Commits.WaitingDirty", FileListText.Describe(_localizer, dirty)));
            }

            return await FastForwardAsync(envelope.Id, commits, quarantine, cancellationToken);
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return PatchApplyResult.Failed(envelope.Id, ex.Message);
        }
        finally
        {
            DeleteQuietly(temp);
        }
    }

    /// <summary>
    /// Moves the checked objects into the repository and the branch to the last commit; takes the objects back out when
    /// the branch cannot move.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="commits">The commits.</param>
    /// <param name="quarantine">The quarantine with the objects.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>Applied or failed.</returns>
    private async Task<PatchApplyResult> FastForwardAsync(Guid patchId, CommitDelivery commits, GitQuarantine quarantine, CancellationToken cancellationToken)
    {
        var moved = _commits.MovePacks(quarantine);
        try
        {
            await _commits.FastForwardAsync(_paths.Workdir, commits.TipCommit, cancellationToken);
        }
        catch (GitExecutionException ex)
        {
            _commits.RemovePacks(moved);
            return PatchApplyResult.Failed(patchId, LocalizedText.Encode("Commits.FastForwardFailed", ex.Message));
        }

        // The sync base of an Origin is its HEAD, which has just moved.
        try
        {
            await _syncBase.EnsureAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            // The branch has moved; the next status request builds the base again.
        }

        return PatchApplyResult.Applied(patchId, commits.Commits.Count);
    }

    /// <summary>
    /// Compares the checked out branch and its last commit with those the commits are built on.
    /// </summary>
    /// <param name="commits">The commits.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encoded reason they differ, or <see langword="null"/> when they are the same.</returns>
    private async Task<string?> FindMismatchAsync(CommitDelivery commits, CancellationToken cancellationToken)
    {
        var workdir = _paths.Workdir;
        if (!await _git.IsWorkTreeAsync(workdir, cancellationToken))
        {
            return LocalizedText.Encode("Commits.NoRepository");
        }

        var branch = await _git.GetCurrentBranchAsync(workdir, cancellationToken);
        var head = await _git.GetHeadCommitAsync(workdir, cancellationToken);
        return branch == commits.Branch && head == commits.BaseCommit
            ? null
            : LocalizedText.Encode("Commits.VersionMismatch", branch ?? "-", head is null ? "-" : head[..10], commits.Branch, commits.BaseCommit[..10]);
    }

    /// <summary>
    /// Checks the unpacked bundle against the signed commits: one reference at the last commit, exactly the listed commits
    /// on the first-parent chain from the base, and only safe paths in every commit it brings.
    /// </summary>
    /// <param name="commits">The commits.</param>
    /// <param name="quarantine">The quarantine with the objects.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The encoded problem, or <see langword="null"/> when the bundle is what was signed.</returns>
    private async Task<string?> FindContentProblemAsync(CommitDelivery commits, GitQuarantine quarantine, CancellationToken cancellationToken)
    {
        var workdir = _paths.Workdir;
        if (quarantine.Heads.Count != 1 || quarantine.Heads[0].Commit != commits.TipCommit)
        {
            return LocalizedText.Encode("Commits.BundleMismatch");
        }

        var chain = await _commits.GetFirstParentChainAsync(workdir, commits.BaseCommit, commits.TipCommit, quarantine, cancellationToken);
        if (chain.Count == 0 ||
            chain[0].FirstParent != commits.BaseCommit ||
            !chain.Select(link => link.Hash).SequenceEqual(commits.Commits.Select(commit => commit.Hash), StringComparer.Ordinal))
        {
            return LocalizedText.Encode("Commits.BundleMismatch");
        }

        foreach (var path in await _commits.GetTouchedPathsAsync(workdir, commits.BaseCommit, commits.TipCommit, quarantine, cancellationToken))
        {
            if (!IsSafePath(path))
            {
                return LocalizedText.Encode("Commits.UnsafePath", path);
            }
        }

        return null;
    }

    /// <summary>
    /// Checks a path the commits touch with the rules a path of a patch must follow.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns><see langword="true"/> for a canonical path inside the working tree and outside Git metadata.</returns>
    private bool IsSafePath(string path)
    {
        try
        {
            return _pathUtility.NormalizeRelativePath(path) == path;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Copies the stored bundle of a delivery to a file Git can read.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="sha256">The hash of the bundle.</param>
    /// <param name="destination">The file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private async Task CopyBundleAsync(Guid patchId, string sha256, string destination, CancellationToken cancellationToken)
    {
        await using var source = await _storage.OpenContentAsync(patchId, sha256, cancellationToken);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(target, cancellationToken);
    }

    /// <summary>
    /// Deletes a temporary folder, ignoring failures.
    /// </summary>
    /// <param name="directory">The folder.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                // Git writes packs read-only, and Windows refuses to delete a read-only file.
                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }
    }
}
