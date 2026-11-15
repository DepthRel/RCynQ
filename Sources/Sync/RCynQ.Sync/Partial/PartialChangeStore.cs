using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Sync.Paths;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Partial;

/// <summary>
/// The choices of a part of the changes of the files of the repository the node works with now, and the check that they
/// still fit.
/// </summary>
/// <remarks>
/// A choice fits while the sync base holds the version of the peer it was made on and the file is still among the pending
/// changes. A choice made on another version of the peer is dropped, and the user hears of it once
/// (<see cref="TakeResetNotices"/>); a choice of a file that has nothing to send any more, or whose working file became
/// the chosen version, is dropped without a word.
/// </remarks>
public sealed class PartialChangeStore
{
    /// <summary>The stored choices.</summary>
    private readonly IPartialChangeRepository _repository;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The files whose choice was dropped because the version of the peer changed, by repository, not told yet.</summary>
    private readonly List<(Guid Repository, string Path)> _resets = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="PartialChangeStore"/> class.
    /// </summary>
    /// <param name="repository">The stored choices.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    public PartialChangeStore(IPartialChangeRepository repository, SyncPaths paths, IPathUtility pathUtility)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _pathUtility = pathUtility ?? throw new ArgumentNullException(nameof(pathUtility));
    }

    /// <summary>
    /// Gets the identifier of the repository the node works with now.
    /// </summary>
    public Guid RepositoryId => _paths.Repository?.Id ?? RepositoryEntry.DefaultId;

    /// <summary>
    /// Finds the choice of a file.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The choice, or <see langword="null"/> when the whole file goes.</returns>
    public Task<PartialChange?> GetAsync(string path, CancellationToken cancellationToken = default)
        => _repository.GetAsync(RepositoryId, path, cancellationToken);

    /// <summary>
    /// Stores the choice of a file.
    /// </summary>
    /// <param name="change">The choice.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the choice is stored.</returns>
    public Task SaveAsync(PartialChange change, CancellationToken cancellationToken = default)
        => _repository.SaveAsync(change, cancellationToken);

    /// <summary>
    /// Forgets the choice of a file.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when there was a choice.</returns>
    public Task<bool> RemoveAsync(string path, CancellationToken cancellationToken = default)
        => _repository.RemoveAsync(RepositoryId, path, cancellationToken);

    /// <summary>
    /// Drops the choices that no longer fit and returns the others.
    /// </summary>
    /// <param name="pending">The pending changes: what the working tree has that the peer lacks.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The choices that fit, by path.</returns>
    /// <remarks>The caller holds the working tree lock and has made sure the sync base is ready.</remarks>
    public async Task<IReadOnlyDictionary<string, PartialChange>> ReconcileAsync(IReadOnlyList<FileChange> pending, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(pending);

        var repositoryId = RepositoryId;
        var stored = await _repository.GetAllAsync(repositoryId, cancellationToken);
        var fitting = new Dictionary<string, PartialChange>(SyncBaseManager.PathComparer);
        if (stored.Count == 0)
        {
            return fitting;
        }

        var pendingByPath = pending.ToDictionary(change => change.Path, SyncBaseManager.PathComparer);
        foreach (var change in stored)
        {
            if (await BaseHashAsync(change.Path, cancellationToken) != change.BaseSha256)
            {
                await _repository.RemoveAsync(repositoryId, change.Path, cancellationToken);
                lock (_resets)
                {
                    _resets.Add((repositoryId, change.Path));
                }

                continue;
            }

            var stillPending = pendingByPath.TryGetValue(change.Path, out var pendingChange) && pendingChange.Kind != ChangeKind.Deleted;
            if (!stillPending || pendingChange!.Sha256 == DefaultStateHasher.Sha256Hex(change.Content))
            {
                await _repository.RemoveAsync(repositoryId, change.Path, cancellationToken);
                continue;
            }

            fitting[change.Path] = change;
        }

        return fitting;
    }

    /// <summary>
    /// Finds the choices of some files that were made on the version of the peer the sync base holds now, without
    /// dropping any.
    /// </summary>
    /// <param name="paths">The paths relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The fitting choices, by path.</returns>
    public async Task<IReadOnlyDictionary<string, PartialChange>> GetFittingAsync(IEnumerable<string> paths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        var wanted = paths.ToHashSet(SyncBaseManager.PathComparer);
        var fitting = new Dictionary<string, PartialChange>(SyncBaseManager.PathComparer);
        foreach (var change in await _repository.GetAllAsync(RepositoryId, cancellationToken))
        {
            if (wanted.Contains(change.Path) && await BaseHashAsync(change.Path, cancellationToken) == change.BaseSha256)
            {
                fitting[change.Path] = change;
            }
        }

        return fitting;
    }

    /// <summary>
    /// Tells whether a fitting choice still matches the working file it was made on.
    /// </summary>
    /// <param name="change">The choice.</param>
    /// <param name="workdirSha256">The SHA-256 of the working file now.</param>
    /// <returns><see cref="PartialChangeState.Partial"/>, or <see cref="PartialChangeState.Outdated"/> when the file was edited since.</returns>
    public static PartialChangeState StateOf(PartialChange change, string? workdirSha256)
    {
        ArgumentNullException.ThrowIfNull(change);
        return string.Equals(change.WorkdirSha256, workdirSha256, StringComparison.Ordinal) ? PartialChangeState.Partial : PartialChangeState.Outdated;
    }

    /// <summary>
    /// Takes the files of the repository the node works with now whose choice was dropped because the version of the
    /// peer changed; each is returned once.
    /// </summary>
    /// <returns>The paths.</returns>
    public IReadOnlyList<string> TakeResetNotices()
    {
        var repositoryId = RepositoryId;
        lock (_resets)
        {
            var taken = _resets.Where(reset => reset.Repository == repositoryId).Select(reset => reset.Path).Distinct(SyncBaseManager.PathComparer).ToArray();
            _resets.RemoveAll(reset => reset.Repository == repositoryId);
            return taken;
        }
    }

    /// <summary>
    /// Computes the SHA-256 of the sync base file of a path.
    /// </summary>
    /// <param name="path">The path relative to the working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The hash, or <see langword="null"/> when the base has no such file.</returns>
    public async Task<string?> BaseHashAsync(string path, CancellationToken cancellationToken = default)
    {
        var full = _pathUtility.ResolveWithinRoot(_paths.SyncBase, path);
        return File.Exists(full) ? await DefaultStateHasher.Sha256FileAsync(full, cancellationToken) : null;
    }
}
