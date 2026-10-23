using System.Text.RegularExpressions;

namespace RCynQ.Core.Validation;

/// <summary>
/// Tells whether names that are passed to Git or come over the network are safe: full commit names and branch names.
/// </summary>
public static partial class GitNames
{
    /// <summary>
    /// Checks whether a text is a full commit name: 40 (SHA-1) or 64 (SHA-256) lower-case hexadecimal digits.
    /// </summary>
    /// <param name="value">The text.</param>
    /// <returns><see langword="true"/> for a full commit name.</returns>
    public static bool IsCommit(string? value) => value is not null && CommitName().IsMatch(value);

    /// <summary>
    /// Checks whether a branch name is safe to pass to Git: up to 100 characters of letters, digits and <c>. _ / -</c>,
    /// not starting with a punctuation mark and without <c>..</c>.
    /// </summary>
    /// <param name="value">The branch name.</param>
    /// <returns><see langword="true"/> for a safe branch name.</returns>
    public static bool IsBranch(string? value)
        => value is not null && BranchName().IsMatch(value) && !value.Contains("..", StringComparison.Ordinal);

    /// <summary>
    /// Matches a full commit name.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^(?:[0-9a-f]{40}|[0-9a-f]{64})$")]
    private static partial Regex CommitName();

    /// <summary>
    /// Matches the characters a safe branch name may have.
    /// </summary>
    /// <returns>The compiled pattern.</returns>
    [GeneratedRegex("^[A-Za-z0-9_][A-Za-z0-9._/-]{0,99}$")]
    private static partial Regex BranchName();
}
