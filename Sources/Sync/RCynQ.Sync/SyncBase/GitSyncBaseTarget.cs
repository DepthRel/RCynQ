namespace RCynQ.Sync.SyncBase;

/// <summary>
/// The Git commit a node's sync base follows, or why there is none.
/// </summary>
/// <param name="IsRepository">Whether the working tree is a Git repository.</param>
/// <param name="Branch">The branch the base follows, or <see langword="null"/> for a detached HEAD or when there is no commit.</param>
/// <param name="Commit">The full name of the commit, or <see langword="null"/> when there is none.</param>
/// <param name="Problem">Why there is no commit, in the language of the request; <see langword="null"/> when there is one.</param>
public sealed record GitSyncBaseTarget(bool IsRepository, string? Branch, string? Commit, string? Problem);
