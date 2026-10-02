namespace RCynQ.Git.Repository;

/// <summary>
/// A file that differs between two commits.
/// </summary>
/// <param name="Deleted"><see langword="true"/> when the file no longer exists in the newer commit.</param>
/// <param name="Path">The path relative to the root of the repository, with <c>/</c> separators.</param>
public sealed record GitPathChange(bool Deleted, string Path);
