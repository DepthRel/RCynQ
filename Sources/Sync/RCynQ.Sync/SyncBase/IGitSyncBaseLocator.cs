namespace RCynQ.Sync.SyncBase;

/// <summary>
/// Finds the Git commit a node's sync base follows.
/// </summary>
public interface IGitSyncBaseLocator
{
    /// <summary>
    /// Finds the commit.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The commit, or why there is none.</returns>
    Task<GitSyncBaseTarget> LocateAsync(CancellationToken cancellationToken = default);
}
