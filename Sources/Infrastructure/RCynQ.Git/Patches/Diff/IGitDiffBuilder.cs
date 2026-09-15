using RCynQ.Core.Exceptions;
using RCynQ.Core.Validation;
using RCynQ.Git.Commands;

namespace RCynQ.Git.Patches.Diff;

/// <summary>
/// Builds a unified binary diff between the <c>a</c> and <c>b</c> subdirectories of a staging directory.
/// </summary>
public interface IGitDiffBuilder
{
    /// <summary>
    /// Runs <c>git diff --no-index --binary</c> between <c>&lt;staging&gt;/a</c> and <c>&lt;staging&gt;/b</c>.
    /// </summary>
    /// <param name="stagingDirectory">The staging directory; missing <c>a</c>/<c>b</c> subdirectories are created empty.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The raw diff bytes with standard <c>a/</c> and <c>b/</c> path prefixes; empty when both sides are equal.</returns>
    /// <exception cref="GitExecutionException">git reported an error.</exception>
    Task<byte[]> BuildAsync(string stagingDirectory, CancellationToken cancellationToken = default);
}
