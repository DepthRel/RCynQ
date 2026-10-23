namespace RCynQ.Git.Repository;

/// <summary>
/// What bringing the remote-tracking branches in line with another repository changed.
/// </summary>
/// <param name="Added">The branches that appeared.</param>
/// <param name="Moved">The branches that now point at another commit.</param>
/// <param name="Removed">The branches that were removed because the other repository no longer has them.</param>
public sealed record GitRemoteBranchChanges(IReadOnlyList<string> Added, IReadOnlyList<string> Moved, IReadOnlyList<string> Removed)
{
    /// <summary>Gets a result that changed nothing.</summary>
    public static GitRemoteBranchChanges None { get; } = new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

    /// <summary>Gets a value indicating whether anything changed.</summary>
    public bool HasChanges => Added.Count > 0 || Moved.Count > 0 || Removed.Count > 0;
}
