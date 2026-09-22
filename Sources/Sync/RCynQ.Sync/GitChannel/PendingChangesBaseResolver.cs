using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Git.Repository;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Resolves the directory a Source node's working tree is compared against to decide what is pending to send.
/// </summary>
public interface IPendingChangesBaseResolver
{
    /// <summary>
    /// Resolves the comparison directory.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The directory to compare against; <see langword="null"/> when the working tree is a Git repository that has not been
    /// pulled from Dest yet and no sync-base snapshot exists either.
    /// </returns>
    Task<PendingChangesBase?> ResolveAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// The directory a working tree is compared against, and whether it must be discarded after use.
/// </summary>
public sealed class PendingChangesBase : IAsyncDisposable
{
    /// <summary>Whether <see cref="Directory"/> is a temporary export this instance owns.</summary>
    private readonly bool _ownsDirectory;

    /// <summary>
    /// Initializes a new instance of the <see cref="PendingChangesBase"/> class.
    /// </summary>
    /// <param name="directory">The directory to compare against.</param>
    /// <param name="isGitNative">
    /// <see langword="true"/> when <paramref name="directory"/> is a temporary export of the branch last pulled from Dest;
    /// <see langword="false"/> when it is the persisted sync-base snapshot.
    /// </param>
    /// <param name="ownsDirectory">
    /// <see langword="true"/> when <see cref="DisposeAsync"/> must delete <paramref name="directory"/>; <see langword="false"/>
    /// for the sync-base snapshot, which this type never deletes.
    /// </param>
    public PendingChangesBase(string directory, bool isGitNative, bool ownsDirectory)
    {
        Directory = directory;
        IsGitNative = isGitNative;
        _ownsDirectory = ownsDirectory;
    }

    /// <summary>Gets the directory to compare the working tree against.</summary>
    public string Directory { get; }

    /// <summary>
    /// Gets a value indicating whether <see cref="Directory"/> is a temporary export of the branch last pulled from Dest
    /// (as opposed to the persisted sync-base snapshot).
    /// </summary>
    public bool IsGitNative { get; }

    /// <summary>
    /// Deletes the temporary export directory this instance owns; a no-op for the sync-base snapshot.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public ValueTask DisposeAsync()
    {
        if (_ownsDirectory && System.IO.Directory.Exists(Directory))
        {
            try
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
            catch (IOException)
            {
                // Temporary export; a later cleanup removes it.
            }
            catch (UnauthorizedAccessException)
            {
                // Temporary export; a later cleanup removes it.
            }
        }

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// Default <see cref="IPendingChangesBaseResolver"/>.
/// </summary>
/// <remarks>
/// A Source node that is a Git working tree and has pulled from Dest at least once is compared against the branch last
/// pulled from Dest (<see cref="GitChannelService.RemoteName"/>). Exporting that tree from Git is not free on a large
/// repository, so the export is cached by the commit it was taken from and reused for every call until a new Pull moves the
/// tracking ref — a status refresh right after another no longer pays for a fresh export. Every other case (Dest, a
/// non-Git working tree, or a Source that has not pulled yet) falls back to the sync-base snapshot, unchanged from before
/// this type existed.
/// </remarks>
public sealed class GitPendingChangesBaseResolver : IPendingChangesBaseResolver, IAsyncDisposable
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The node data directories.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>Serializes cache reads and refreshes, so a concurrent call never sees a half-written export.</summary>
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    /// <summary>Exports superseded by a newer commit, kept until disposal in case a caller is still reading them.</summary>
    private readonly List<string> _staleExports = new();

    /// <summary>The commit the cached export was taken from.</summary>
    private string? _cachedCommit;

    /// <summary>The directory of the cached export.</summary>
    private string? _cachedExport;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPendingChangesBaseResolver"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="appData">The node data directories.</param>
    public GitPendingChangesBaseResolver(NodeConfig node, SyncPaths paths, IGitRepositoryService git, IAppDataFactory appData)
    {
        _node = node;
        _paths = paths;
        _git = git;
        _appData = appData;
    }

    /// <inheritdoc />
    public async Task<PendingChangesBase?> ResolveAsync(CancellationToken cancellationToken = default)
    {
        if (_node.Role == NodeRole.Source && _node.EffectiveWorkdir is { } workdir)
        {
            var branch = await _git.GetBranchInfoAsync(workdir, GitChannelService.RemoteName, cancellationToken);
            if (branch.TrackingRef is not null)
            {
                var export = await GetOrExportAsync(workdir, branch.TrackingRef, cancellationToken);
                return new PendingChangesBase(export, isGitNative: true, ownsDirectory: false);
            }
        }

        return _paths.BaselineInitialized
            ? new PendingChangesBase(_paths.SyncBase, isGitNative: false, ownsDirectory: false)
            : null;
    }

    /// <summary>
    /// Returns the cached export of <paramref name="trackingRef"/> when it is still current, otherwise exports it fresh.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="trackingRef">The branch last pulled from Dest.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The directory of the export.</returns>
    private async Task<string> GetOrExportAsync(string workdir, string trackingRef, CancellationToken cancellationToken)
    {
        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            var commit = await _git.ResolveCommitAsync(workdir, trackingRef, cancellationToken) ?? trackingRef;
            if (_cachedCommit == commit && _cachedExport is not null && Directory.Exists(_cachedExport))
            {
                return _cachedExport;
            }

            var export = Path.Combine(_appData.Create().TempDirectory, "pending-" + Guid.NewGuid().ToString("N"));
            await _git.ExportTreeAsync(workdir, trackingRef, export, cancellationToken);
            if (_cachedExport is not null)
            {
                // Not deleted here: a caller that started reading the previous export just before this replacement must not
                // have its files vanish from under it. Cleared on disposal instead.
                _staleExports.Add(_cachedExport);
            }

            _cachedCommit = commit;
            _cachedExport = export;
            return export;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    /// <summary>
    /// Deletes every export this resolver ever created.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        await _cacheLock.WaitAsync();
        try
        {
            foreach (var directory in _staleExports)
            {
                DeleteQuietly(directory);
            }

            _staleExports.Clear();
            if (_cachedExport is not null)
            {
                DeleteQuietly(_cachedExport);
                _cachedExport = null;
            }
        }
        finally
        {
            _cacheLock.Release();
        }

        _cacheLock.Dispose();
    }

    /// <summary>
    /// Deletes a directory, ignoring failures.
    /// </summary>
    /// <param name="directory">The directory.</param>
    private static void DeleteQuietly(string directory)
    {
        try
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary export; a later cleanup removes it.
        }
        catch (UnauthorizedAccessException)
        {
            // Temporary export; a later cleanup removes it.
        }
    }
}
