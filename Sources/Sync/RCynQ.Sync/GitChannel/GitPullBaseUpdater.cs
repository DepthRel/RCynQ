using RCynQ.Core.Abstractions.Paths;
using RCynQ.Git.Repository;
using RCynQ.Sync.Paths;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Brings the sync base in line with the files a <c>pull</c> from Dest changed in the working tree.
/// </summary>
/// <remarks>
/// <para>
/// A pull replaces files that Dest already has. Without this step the sync base would still hold the old versions and every pulled file
/// would look like a local change to send back to Dest.
/// </para>
/// <para>
/// Only files the pull changed are touched. A fast-forward pull refuses to overwrite a file with uncommitted local edits, so a pulled
/// file in the working tree is exactly the version Dest has; edits the user made to other files stay pending.
/// </para>
/// </remarks>
public sealed class GitPullBaseUpdater
{
    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPullBaseUpdater"/> class.
    /// </summary>
    /// <param name="paths">The sync paths.</param>
    /// <param name="pathUtility">The path utility.</param>
    public GitPullBaseUpdater(SyncPaths paths, IPathUtility pathUtility)
    {
        _paths = paths;
        _pathUtility = pathUtility;
    }

    /// <summary>
    /// Copies the pulled files from the working tree into the sync base and removes the deleted ones.
    /// </summary>
    /// <param name="changes">The files the pull changed.</param>
    /// <param name="workdir">The working tree.</param>
    /// <returns>The number of files brought in line; zero when there is no sync base yet.</returns>
    public int Update(IReadOnlyList<GitPathChange> changes, string workdir)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (!_paths.BaselineInitialized)
        {
            return 0;
        }

        var count = 0;
        foreach (var change in changes)
        {
            string target;
            string source;
            try
            {
                // Git metadata, absolute paths and paths that leave the tree are refused here, as everywhere else.
                target = _pathUtility.ResolveWithinRoot(_paths.SyncBase, change.Path);
                source = _pathUtility.ResolveWithinRoot(workdir, change.Path);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (change.Deleted)
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                    count++;
                }

                continue;
            }

            if (!File.Exists(source) || new FileInfo(source).LinkTarget is not null)
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, overwrite: true);
            count++;
        }

        return count;
    }
}
