using RCynQ.Core.State;

namespace RCynQ.Core.Api;

/// <summary>
/// Result of a Git channel operation.
/// </summary>
/// <param name="Succeeded">Whether the operation succeeded.</param>
/// <param name="Output">The git output or error.</param>
/// <param name="Branch">The branch state after the operation, or <see langword="null"/>.</param>
public sealed record GitOperationResult(bool Succeeded, string Output, GitBranchInfo? Branch);
