namespace RCynQ.Core.Api;

/// <summary>
/// A branch of a repository and the commit it points at.
/// </summary>
/// <param name="Name">The name of the branch without <c>refs/heads/</c>, for example <c>feature/login</c>.</param>
/// <param name="Commit">The full object name of the commit (40 or 64 hexadecimal characters).</param>
public sealed record GitBranchTip(string Name, string Commit);
