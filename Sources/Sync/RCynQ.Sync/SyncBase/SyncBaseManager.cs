using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.State;

namespace RCynQ.Sync.SyncBase;

/// <summary>
/// Keeps the sync base of the node: the state this node believes its peer has.
/// </summary>
/// <remarks>
/// <para>
/// The working tree is compared against the sync base to find what to send, and an incoming change is applied into the sync base
/// as well as into the working tree, because afterwards both nodes have it.
/// </para>
/// <para>
/// For a Git working tree the base is built automatically: it is exported from the commit both nodes have
/// (<see cref="IGitSyncBaseLocator"/>), and exported again whenever that commit changes, for example after a Pull on Workstation or a
/// commit on Origin. Files exchanged with the peer since the last export are carried over into the new export (each remembered with a hash of what the peer has, and only while the base still matches it), unless the new commit
/// already has them or, on Workstation, the new commit changed them (the commit is then the newer word on what Origin has).
/// Nothing has to be done by hand, and no working trees have to be made identical first.
/// </para>
/// <para>
/// A working tree without Git keeps the manual snapshot of earlier versions: the user takes it when both working trees match.
/// A manual snapshot of a working tree that is (or becomes) a Git repository is not trusted: it is replaced by the commit.
/// </para>
/// <para>
/// <see cref="EnsureAsync"/> and <see cref="RecordExchangeAsync"/> must be called with the <see cref="WorkingTreeLock"/> held, so
/// the base never changes under a reader; <see cref="RebuildAsync"/> takes the lock itself.
/// </para>
/// </remarks>
public sealed class SyncBaseManager
{
    /// <summary>The suffix of the directory a new base is built in before it is swapped into place.</summary>
    private const string STAGING_SUFFIX = ".new-";

    /// <summary>The suffix of the directory the replaced base is parked in until it is deleted.</summary>
    private const string RETIRED_SUFFIX = ".old-";

    /// <summary>
    /// How long a staging directory is left alone before it counts as the leftover of a process that was killed.
    /// </summary>
    /// <remarks>
    /// The working tree lock only serializes exports inside one process, so the CLI may be exporting against the same data
    /// root right now. No export of a working tree takes anywhere near this long, so waiting it out costs nothing and makes
    /// the cleanup unable to delete a directory that is still being filled.
    /// </remarks>
    private static readonly TimeSpan _abandonedAfter = TimeSpan.FromHours(1);

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>Whether the leftovers of a killed process were already looked for; <c>1</c> once they were.</summary>
    private int _stagingSwept;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The locator of the Git commit.</summary>
    private readonly IGitSyncBaseLocator _locator;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The metadata store.</summary>
    private readonly SyncBaseMetadataStore _metadata;

    /// <summary>The manifest builder.</summary>
    private readonly IFileManifestBuilder _manifest;

    /// <summary>The state hasher.</summary>
    private readonly IStateHasher _hasher;

    /// <summary>The snapshot repository.</summary>
    private readonly ISnapshotRepository _snapshots;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The time provider.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncBaseManager"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="locator">The locator of the Git commit.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="metadata">The metadata store.</param>
    /// <param name="manifest">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="snapshots">The snapshot repository.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="timeProvider">The time provider.</param>
    public SyncBaseManager(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IGitSyncBaseLocator locator,
        IGitRepositoryService git,
        SyncBaseMetadataStore metadata,
        IFileManifestBuilder manifest,
        IStateHasher hasher,
        ISnapshotRepository snapshots,
        IPathUtility pathUtility,
        WorkingTreeLock workingTreeLock,
        TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _locator = locator;
        _git = git;
        _metadata = metadata;
        _manifest = manifest;
        _hasher = hasher;
        _snapshots = snapshots;
        _pathUtility = pathUtility;
        _lock = workingTreeLock;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Makes sure the sync base matches the current Git commit, exporting it again when the commit changed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status of the sync base; <see cref="SyncBaseStatus.IsReady"/> tells whether patches can be exchanged.</returns>
    /// <exception cref="SyncBridgeException">The base could not be exported.</exception>
    /// <remarks>The caller must hold the <see cref="WorkingTreeLock"/>.</remarks>
    public async Task<SyncBaseStatus> EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            return SyncBaseStatus.NotApplicable;
        }

        if (Interlocked.Exchange(ref _stagingSwept, 1) == 0)
        {
            // Once per process: leftovers can only appear when a process dies, and a node that never exports again
            // would otherwise keep them for good.
            DeleteAbandonedStaging();
        }

        var target = await _locator.LocateAsync(cancellationToken);
        var metadata = await _metadata.ReadAsync(cancellationToken);
        var baseExists = Directory.Exists(_paths.SyncBase);
        if (target.Commit is not null)
        {
            if (!(baseExists && metadata is { Mode: SyncBaseMode.Git, Format: SyncBaseMetadata.CURRENT_FORMAT } && metadata.Branch == target.Branch && metadata.Commit == target.Commit))
            {
                await ExportAsync(target, metadata, baseExists, cancellationToken);
            }

            return new SyncBaseStatus(SyncBaseMode.Git, target.Branch, target.Commit, null, false) { ChangesSince = target.ChangesSince };
        }

        // No commit to build from. A manual snapshot (also one of an older version, which has no metadata) keeps working;
        // a base exported from Git for a commit that is gone does not, because nothing says what the peer has any more.
        var canCreateManual = !target.IsRepository;
        if (baseExists && metadata is null or { Mode: SyncBaseMode.Manual })
        {
            return new SyncBaseStatus(SyncBaseMode.Manual, null, null, null, canCreateManual);
        }

        return SyncBaseStatus.NotReady(target.Problem ?? _localizer.Get("Patch.NoBaseline"), canCreateManual);
    }

    /// <summary>
    /// Notes that files of the sync base now hold what was exchanged with the peer, so a new export keeps them.
    /// </summary>
    /// <param name="paths">The relative paths that were written into the sync base.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the note is stored.</returns>
    /// <remarks>The caller must hold the <see cref="WorkingTreeLock"/>.</remarks>
    public async Task RecordExchangeAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var metadata = await _metadata.ReadAsync(cancellationToken);
        if (metadata is not { Mode: SyncBaseMode.Git, Format: SyncBaseMetadata.CURRENT_FORMAT })
        {
            // A manual snapshot is itself the whole record of what the peer has.
            return;
        }

        // The record is what the base holds for the path right now (the caller has just written it there), as a hash.
        var exchanged = new Dictionary<string, string?>(metadata.Exchanged, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            exchanged[path] = await ContentHashOfAsync(_paths.SyncBase, path, cancellationToken);
        }

        await _metadata.WriteAsync(metadata with { Exchanged = exchanged }, cancellationToken);
    }

    /// <summary>
    /// Narrows the byte-level differences between the sync base and the working tree to the changes the peer really lacks, and
    /// records in each of them the content it was built from.
    /// </summary>
    /// <param name="differences">The differences found by comparing the sync base with the working tree byte by byte.</param>
    /// <param name="syncBase">The status returned by <see cref="EnsureAsync"/>; must be ready.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The pending changes, with <see cref="Core.Patches.FileChange.BaseSha256"/> set to the line-ending-insensitive hash of the base file.</returns>
    /// <remarks>
    /// <para>
    /// For a Git base only files Git itself reports as changed since the base commit (as the user's <c>git status</c> would) can
    /// be pending, so the list always follows the current state of the working tree. A file that differs from the export only in
    /// how it is checked out (line endings, filters) is never offered.
    /// </para>
    /// <para>
    /// For any base, a file whose content differs only in CRLF versus LF is not a change.
    /// </para>
    /// <para>The caller must hold the <see cref="WorkingTreeLock"/>.</para>
    /// </remarks>
    public async Task<IReadOnlyList<Core.Patches.FileChange>> SelectPendingAsync(
        IReadOnlyList<Core.Patches.FileChange> differences,
        SyncBaseStatus syncBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(differences);
        ArgumentNullException.ThrowIfNull(syncBase);
        if (differences.Count == 0 || !syncBase.IsReady)
        {
            return Array.Empty<Core.Patches.FileChange>();
        }

        ISet<string>? candidates = null;
        if (syncBase is { Mode: SyncBaseMode.Git, Commit: { } commit })
        {
            // Only the current state of the Git working tree decides: a file Git no longer sees as changed (for example one that came
            // from the peer and was then discarded here) is not a change, whatever the sync base still remembers about it. Commits
            // that go as commits are not changes of the working tree either.
            candidates = new HashSet<string>(await _git.GetChangedSinceAsync(_paths.Workdir, syncBase.ChangesSince ?? commit, cancellationToken), PathComparer);
        }

        var pending = new List<Core.Patches.FileChange>();
        foreach (var change in differences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (candidates is not null && !candidates.Contains(change.Path))
            {
                continue;
            }

            var local = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(_paths.Workdir, change.Path), cancellationToken);
            var peer = await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(_paths.SyncBase, change.Path), cancellationToken);
            if (local == peer)
            {
                continue;
            }

            pending.Add(new Core.Patches.FileChange(
                change.Kind,
                change.Path,
                change.Sha256,
                change.SizeBytes,
                change.IsSymlink,
                change.Kind == ChangeKind.Added || peer == FileState.DIRECTORY_MARKER ? null : peer));
        }

        return pending;
    }

    /// <summary>
    /// Lists every file that differs from the Git commit: the pending changes, and the files the peer already has but that are not
    /// committed yet.
    /// </summary>
    /// <param name="pending">The pending changes, see <see cref="SelectPendingAsync"/>.</param>
    /// <param name="syncBase">The status returned by <see cref="EnsureAsync"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files ordered by path (ordinal); without Git only the pending changes.</returns>
    /// <remarks>
    /// A file the peer received does not stay pending, but it still differs from the commit until somebody commits it, so the user
    /// keeps seeing it. The caller must hold the <see cref="WorkingTreeLock"/>.
    /// </remarks>
    public async Task<IReadOnlyList<ChangedFile>> ListChangedAsync(
        IReadOnlyList<Core.Patches.FileChange> pending,
        SyncBaseStatus syncBase,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(syncBase);
        var listed = new Dictionary<string, ChangedFile>(PathComparer);
        foreach (var change in pending)
        {
            listed[change.Path] = new ChangedFile(change.Kind, change.Path, false);
        }

        if (syncBase is { IsReady: true, Mode: SyncBaseMode.Git, Commit: { } commit })
        {
            foreach (var (path, kind) in await _git.GetChangedFilesSinceAsync(_paths.Workdir, syncBase.ChangesSince ?? commit, cancellationToken))
            {
                // What a dead process left of a half-prepared patch is untracked for Git, but it is not a file of the user.
                if (path.Split('/').Any(Git.Paths.PathUtility.IsApplyStagingName))
                {
                    continue;
                }

                listed.TryAdd(path, new ChangedFile(kind, path, true));
            }
        }

        return listed.Values.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
    }

    /// <summary>
    /// Chooses the changes of a patch: the pending changes among the requested files, and for the requested files the peer already
    /// has, the change that sends the same version once more.
    /// </summary>
    /// <param name="pending">The pending changes, see <see cref="SelectPendingAsync"/>.</param>
    /// <param name="syncBase">The status returned by <see cref="EnsureAsync"/>; must be ready.</param>
    /// <param name="paths">The relative paths to send, or <see langword="null"/> for every changed file. Unknown paths are ignored.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The changes ordered by path (ordinal).</returns>
    /// <remarks>
    /// Sending a version again is harmless: a peer whose file already has it applies nothing, and a peer whose file changed since
    /// gets a conflict to resolve instead of an overwrite. The caller must hold the <see cref="WorkingTreeLock"/>.
    /// </remarks>
    public async Task<IReadOnlyList<Core.Patches.FileChange>> SelectForSendAsync(
        IReadOnlyList<Core.Patches.FileChange> pending,
        SyncBaseStatus syncBase,
        IReadOnlyCollection<string>? paths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(syncBase);
        var wanted = paths is null ? null : new HashSet<string>(paths, PathComparer);
        var pendingByPath = pending.ToDictionary(change => change.Path, PathComparer);
        var selected = new List<Core.Patches.FileChange>();
        Dictionary<string, Core.Patches.FileEntry>? local = null;
        foreach (var file in await ListChangedAsync(pending, syncBase, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (wanted is not null && !wanted.Contains(file.Path))
            {
                continue;
            }

            if (pendingByPath.TryGetValue(file.Path, out var change))
            {
                selected.Add(change);
                continue;
            }

            local ??= (await _manifest.ScanAsync(_paths.Workdir, cancellationToken)).ToDictionary(entry => entry.Path, PathComparer);
            local.TryGetValue(file.Path, out var entry);
            if (await ResendAsync(file.Path, entry, cancellationToken) is { } resend)
            {
                selected.Add(resend);
            }
        }

        return selected;
    }

    /// <summary>
    /// Builds the change that sends a version of a file again that the sync base already holds.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <param name="entry">The file in the working tree, or <see langword="null"/> when it was deleted.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The change, or <see langword="null"/> when the peer has nothing to change for the path.</returns>
    private async Task<Core.Patches.FileChange?> ResendAsync(string path, Core.Patches.FileEntry? entry, CancellationToken cancellationToken)
    {
        var peer = await ContentHashOfAsync(_paths.SyncBase, path, cancellationToken);
        if (peer == FileState.DIRECTORY_MARKER)
        {
            return null;
        }

        if (entry is null)
        {
            return peer is null ? null : new Core.Patches.FileChange(ChangeKind.Deleted, path, null, null, false, peer);
        }

        return new Core.Patches.FileChange(peer is null ? ChangeKind.Added : ChangeKind.Modified, path, entry.Sha256, entry.SizeBytes, entry.IsSymlink, peer);
    }

    /// <summary>
    /// Rebuilds the sync base from scratch: for a Git working tree from its commit, forgetting everything remembered about the peer;
    /// for a working tree without Git as a manual snapshot of the working tree.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The recorded snapshot.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree, a Git repository has no commit to build from, or the rebuild failed.</exception>
    public async Task<SnapshotInfo> RebuildAsync(CancellationToken cancellationToken = default)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new SyncBridgeException(_localizer.Get("Baseline.RelayHasNone"));
        }

        var workdir = _paths.Workdir;
        if (!Directory.Exists(workdir))
        {
            throw new SyncBridgeException(_localizer.Format("Baseline.WorkdirMissing", workdir));
        }

        using (await _lock.AcquireAsync(cancellationToken))
        {
            var target = await _locator.LocateAsync(cancellationToken);
            IReadOnlyList<Core.Patches.FileEntry> entries;
            if (target.Commit is not null)
            {
                // A Git working tree: throw away everything remembered about the peer and start again from the commit.
                await ExportAsync(target, null, baseExists: false, cancellationToken);
                entries = await _manifest.ScanAsync(_paths.SyncBase, workdir, cancellationToken);
            }
            else if (target.IsRepository)
            {
                throw new SyncBridgeException(target.Problem ?? _localizer.Get("Patch.NoBaseline"));
            }
            else
            {
                entries = await _manifest.ScanAsync(workdir, cancellationToken);
                await ReplaceAsync(staging =>
                {
                    foreach (var entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        CopyEntry(_pathUtility.ResolveWithinRoot(workdir, entry.Path), _pathUtility.ResolveWithinRoot(staging, entry.Path));
                    }

                    return Task.CompletedTask;
                });
                await _metadata.WriteAsync(SyncBaseMetadata.Manual(), cancellationToken);
            }

            return await _snapshots.CreateAsync(
                new SnapshotInfo(Guid.NewGuid(), _node.Id, _timeProvider.GetUtcNow(), _hasher.ComputeHash(entries), "baseline"),
                cancellationToken);
        }
    }

    /// <summary>
    /// Exports the sync base from a commit and carries over the files exchanged with the peer.
    /// </summary>
    /// <param name="target">The commit.</param>
    /// <param name="previous">The metadata of the current base, or <see langword="null"/>.</param>
    /// <param name="baseExists">Whether a base directory exists.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the new base and its metadata are in place.</returns>
    /// <exception cref="SyncBridgeException">The export failed.</exception>
    private async Task ExportAsync(GitSyncBaseTarget target, SyncBaseMetadata? previous, bool baseExists, CancellationToken cancellationToken)
    {
        var workdir = _paths.Workdir;
        var exchanged = new Dictionary<string, string?>(StringComparer.Ordinal);
        await ReplaceAsync(async staging =>
        {
            await _git.ExportTreeAsync(workdir, target.Commit!, staging, cancellationToken);
            if (!baseExists)
            {
                return;
            }

            foreach (var (path, recorded) in await CarryCandidatesAsync(target, previous, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await CarryAsync(path, recorded, staging, cancellationToken))
                {
                    exchanged[path] = recorded;
                }
            }
        });

        await _metadata.WriteAsync(new SyncBaseMetadata(SyncBaseMode.Git, target.Branch, target.Commit, exchanged, SyncBaseMetadata.CURRENT_FORMAT), cancellationToken);
    }

    /// <summary>
    /// Lists the records of files that may still hold what the peer has rather than what the new commit has.
    /// </summary>
    /// <param name="target">The new commit.</param>
    /// <param name="previous">The metadata of the current base, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The relative paths with what the peer was recorded to have.</returns>
    /// <remarks>
    /// Only the records of a base of the current format on the same branch are trusted. A manual snapshot, a base of an earlier
    /// version and a base of another branch carry nothing over: they say nothing reliable about what the peer has, and trusting
    /// them once made every file added since look like a change to send. Not carrying a record is harmless: the file is offered
    /// once more, and a peer that already has that content applies it as a no-op.
    /// </remarks>
    private async Task<IReadOnlyDictionary<string, string?>> CarryCandidatesAsync(GitSyncBaseTarget target, SyncBaseMetadata? previous, CancellationToken cancellationToken)
    {
        if (previous is not { Mode: SyncBaseMode.Git, Format: SyncBaseMetadata.CURRENT_FORMAT } || previous.Branch != target.Branch || previous.Exchanged.Count == 0)
        {
            return new Dictionary<string, string?>();
        }

        var candidates = new Dictionary<string, string?>(previous.Exchanged, StringComparer.Ordinal);
        if (_node.Role == NodeRole.Workstation && previous.Commit is not null && previous.Commit != target.Commit)
        {
            try
            {
                // Workstation moved to another commit of Origin: for the files that commit changed, it is the newer word on what Origin has.
                foreach (var change in await _git.GetChangedPathsAsync(_paths.Workdir, previous.Commit, target.Commit!, cancellationToken))
                {
                    candidates.Remove(change.Path);
                }
            }
            catch (Exception ex) when (ex is GitExecutionException or ArgumentException)
            {
                // The previous commit is gone; without it nothing tells which records are outdated, so start from the commit alone.
                candidates.Clear();
            }
        }

        return candidates;
    }

    /// <summary>
    /// Copies one file of the current base into the fresh export, when the record about it is still true and the export does not
    /// already have the same content.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <param name="recorded">What the peer was recorded to have: a hash, or <see langword="null"/> for no file.</param>
    /// <param name="staging">The fresh export.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when the file still differs from the commit and was carried over.</returns>
    private async Task<bool> CarryAsync(string path, string? recorded, string staging, CancellationToken cancellationToken)
    {
        string current;
        string fresh;
        try
        {
            current = _pathUtility.ResolveWithinRoot(_paths.SyncBase, path);
            fresh = _pathUtility.ResolveWithinRoot(staging, path);
        }
        catch (ArgumentException)
        {
            return false;
        }

        // Compared without line endings: a file the peer has with other line endings than the checkout is still the same content.
        var currentHash = await FileState.ContentHashAsync(current, cancellationToken);
        var freshHash = await FileState.ContentHashAsync(fresh, cancellationToken);
        if (currentHash != recorded ||
            currentHash == freshHash ||
            currentHash == FileState.DIRECTORY_MARKER ||
            freshHash == FileState.DIRECTORY_MARKER)
        {
            // Not what was recorded (the base was changed behind the record's back), or the commit has it now: trust the commit.
            return false;
        }

        if (currentHash is null)
        {
            File.Delete(fresh);
        }
        else
        {
            if (File.Exists(fresh) || new FileInfo(fresh).LinkTarget is not null)
            {
                File.Delete(fresh);
            }

            CopyEntry(current, fresh);
        }

        return true;
    }

    /// <summary>
    /// Hashes a file below a root without line endings, or <see langword="null"/> when there is none.
    /// </summary>
    /// <param name="root">The root.</param>
    /// <param name="path">The relative path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash.</returns>
    private async Task<string?> ContentHashOfAsync(string root, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await FileState.ContentHashAsync(_pathUtility.ResolveWithinRoot(root, path), cancellationToken);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets the comparer for paths as the file system of the node compares them.
    /// </summary>
    private static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>
    /// Builds a new base in a staging directory and swaps it in place of the current one.
    /// </summary>
    /// <param name="fill">Fills the staging directory; it may read the current base, which is still in place.</param>
    /// <returns>A task that completes when the new base is in place.</returns>
    /// <exception cref="SyncBridgeException">The new base could not be built or moved into place.</exception>
    private async Task ReplaceAsync(Func<string, Task> fill)
    {
        var target = _paths.SyncBase;
        var staging = target + STAGING_SUFFIX + Guid.NewGuid().ToString("N");
        var retired = target + RETIRED_SUFFIX + Guid.NewGuid().ToString("N");
        DeleteAbandonedStaging();
        try
        {
            Directory.CreateDirectory(staging);
            await fill(staging);
            if (Directory.Exists(target))
            {
                Directory.Move(target, retired);

                // A rename keeps the stamps of the old base, which may be days old: without a fresh one the cleanup of
                // another process would take the parked base for a leftover while it may still have to be moved back.
                TouchQuietly(retired);
            }

            Directory.Move(staging, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or GitExecutionException)
        {
            RestoreQuietly(retired, target);
            throw new SyncBridgeException(_localizer.Format("SyncBase.Failed", _localizer.Describe(ex)), ex);
        }
        finally
        {
            DeleteQuietly(staging);

            // Until a base is in place the parked one is the only copy of what the peer has: it is removed only then.
            // (When it could not be moved back it stays where it is, instead of being deleted on the way out.)
            if (Directory.Exists(target))
            {
                DeleteQuietly(retired);
            }
        }
    }

    /// <summary>
    /// Moves the parked base back after a failed swap, ignoring failures: the failure of the swap is what gets reported.
    /// </summary>
    /// <param name="retired">The parked base.</param>
    /// <param name="target">The place of the base.</param>
    private static void RestoreQuietly(string retired, string target)
    {
        try
        {
            if (!Directory.Exists(target) && Directory.Exists(retired))
            {
                Directory.Move(retired, target);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Nothing more can be done here; the parked base is kept, see ReplaceAsync.
        }
    }

    /// <summary>
    /// Copies a file or a symbolic link, creating the parent directory.
    /// </summary>
    /// <param name="source">The source path.</param>
    /// <param name="destination">The destination path; must not exist.</param>
    /// <exception cref="SyncBridgeException">The source stopped being a symbolic link while copying.</exception>
    private static void CopyEntry(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var link = new FileInfo(source).LinkTarget ?? new DirectoryInfo(source).LinkTarget;
        if (link is not null)
        {
            File.CreateSymbolicLink(destination, link);
            return;
        }

        File.Copy(source, destination);
    }

    /// <summary>
    /// Deletes the staging directories a killed process left next to the sync base.
    /// </summary>
    /// <remarks>
    /// An export builds the new base in <see cref="STAGING_SUFFIX"/> and parks the replaced one in
    /// <see cref="RETIRED_SUFFIX"/>, deleting both when it finishes. A process that is killed mid-export runs no cleanup, so
    /// it leaves a partial copy of the working tree behind, once per killed start. Only directories untouched for
    /// <see cref="_abandonedAfter"/> are removed; anything else beside the base is left alone.
    /// </remarks>
    private void DeleteAbandonedStaging()
    {
        var target = _paths.SyncBase;
        var directory = Path.GetDirectoryName(target);
        var prefix = Path.GetFileName(target);
        if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(prefix) || !Directory.Exists(directory))
        {
            return;
        }

        var abandonedBefore = _timeProvider.GetUtcNow() - _abandonedAfter;
        foreach (var candidate in EnumerateQuietly(directory))
        {
            var name = Path.GetFileName(candidate);
            if (!name.StartsWith(prefix + STAGING_SUFFIX, StringComparison.Ordinal) &&
                !name.StartsWith(prefix + RETIRED_SUFFIX, StringComparison.Ordinal))
            {
                continue;
            }

            DateTime touched;
            try
            {
                // The newer of the two stamps, because a file system may not keep a creation time.
                var created = Directory.GetCreationTimeUtc(candidate);
                var written = Directory.GetLastWriteTimeUtc(candidate);
                touched = created > written ? created : written;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort: a leftover that cannot be examined must not fail the export or the check it runs before.
                continue;
            }

            if (touched < abandonedBefore.UtcDateTime)
            {
                DeleteQuietly(candidate);
            }
        }
    }

    /// <summary>
    /// Stamps a directory as written now, ignoring failures, so <see cref="DeleteAbandonedStaging"/> leaves it alone.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    private void TouchQuietly(string directory)
    {
        try
        {
            Directory.SetLastWriteTimeUtc(directory, _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only the age guard of the cleanup depends on the stamp; the swap itself does not.
        }
    }

    /// <summary>
    /// Lists the subdirectories of a directory, returning nothing when it cannot be read.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>The subdirectory paths.</returns>
    private static IReadOnlyList<string> EnumerateQuietly(string directory)
    {
        try
        {
            return Directory.GetDirectories(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Deletes a directory, ignoring failures.
    /// </summary>
    /// <param name="directory">The directory path.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next replacement leaves it behind harmlessly.
        }
    }
}
