namespace RCynQ.Git.Repository;

/// <summary>
/// How an attempt of Git to switch the working tree to another branch ended.
/// </summary>
/// <param name="Succeeded">Whether the working tree is on the branch now.</param>
/// <param name="Created">Whether the local branch was created from the remote-tracking branch of the same name.</param>
/// <param name="BlockingFiles">When Git refused, the files whose changes it would have overwritten; otherwise empty.</param>
/// <param name="Output">What Git wrote.</param>
public sealed record GitSwitchAttempt(bool Succeeded, bool Created, IReadOnlyList<string> BlockingFiles, string Output);
