using System.Globalization;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.Backups;

/// <summary>
/// Backs up local files before they are overwritten.
/// </summary>
public sealed class BackupService
{
    /// <summary>
    /// The sync paths.
    /// </summary>
    private readonly SyncPaths _syncPaths;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="BackupService"/> class.
    /// </summary>
    /// <param name="syncPaths">The sync paths.</param>
    /// <param name="paths">The path utility.</param>
    /// <param name="timeProvider">The clock.</param>
    public BackupService(SyncPaths syncPaths, IPathUtility paths, TimeProvider timeProvider)
    {
        _syncPaths = syncPaths;
        _paths = paths;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Copies the existing files to <c>backups/&lt;patchId&gt;/&lt;timestamp&gt;/</c>.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="workdir">The working tree.</param>
    /// <param name="relativePaths">The relative paths to back up; missing files are skipped.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The backup directory, or <see langword="null"/> when nothing was copied.</returns>
    public async Task<string?> BackupAsync(Guid patchId, string workdir, IEnumerable<string> relativePaths, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relativePaths);
        var target = Path.Combine(
            _syncPaths.Backups,
            patchId.ToString("N"),
            _timeProvider.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture));
        var copied = 0;
        foreach (var relative in relativePaths.Distinct(StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = _paths.ResolveWithinRoot(workdir, relative);
            var info = new FileInfo(source);
            if (info.LinkTarget is { } link)
            {
                var destination = _paths.ResolveWithinRoot(target, relative + ".symlink");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await File.WriteAllTextAsync(destination, link, cancellationToken);
            }
            else if (info.Exists)
            {
                var destination = _paths.ResolveWithinRoot(target, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            else
            {
                continue;
            }

            copied++;
        }

        return copied == 0 ? null : target;
    }
}
