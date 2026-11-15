using RCynQ.Core.Abstractions.Paths;
using RCynQ.Localization;

namespace RCynQ.Sync.Partial;

/// <summary>
/// Checks the path of a changed file that came in a request and brings it to the form of the list of changes.
/// </summary>
internal static class ChangePath
{
    /// <summary>
    /// Checks a path from a request and brings it to the form of the list of changes.
    /// </summary>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="workdir">The full path of the working tree.</param>
    /// <param name="path">The path from the request.</param>
    /// <returns>The path relative to the working tree with <c>/</c> separators.</returns>
    /// <exception cref="ArgumentException">The path is blank or leads out of the working tree.</exception>
    public static string Normalize(IPathUtility pathUtility, string workdir, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(LocalizedError.Message("Changes.NoPath"), nameof(path));
        }

        // Refuses a path out of the working tree or into the Git metadata.
        var normalized = pathUtility.NormalizeRelativePath(path);
        pathUtility.ResolveWithinRoot(workdir, normalized);
        return normalized;
    }
}
