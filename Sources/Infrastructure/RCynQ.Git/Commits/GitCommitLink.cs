namespace RCynQ.Git.Commits;

/// <summary>
/// A commit and its first parent, as one link of a first-parent chain.
/// </summary>
/// <param name="Hash">The full commit name.</param>
/// <param name="FirstParent">The first parent, or <see langword="null"/> for a root commit.</param>
public sealed record GitCommitLink(string Hash, string? FirstParent);
