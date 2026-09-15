namespace RCynQ.Core.Abstractions.Paths;

/// <summary>
/// Provides safe relative-path handling for working directories.
/// </summary>
/// <remarks>
/// Implementations must prevent path traversal outside the declared root.
/// </remarks>
public interface IPathUtility
{
    /// <summary>
    /// Normalizes a relative path (separators, redundant segments).
    /// </summary>
    /// <param name="relativePath">The relative path to normalize.</param>
    /// <returns>The normalized relative path.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="relativePath"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="relativePath"/> is blank, is absolute, or escapes the working root.
    /// </exception>
    string NormalizeRelativePath(string relativePath);

    /// <summary>
    /// Checks whether the given target path resolves within the given root path.
    /// </summary>
    /// <param name="rootPath">The root directory path.</param>
    /// <param name="targetPath">The candidate path to check.</param>
    /// <returns><see langword="true"/> when the target is inside the root; otherwise <see langword="false"/>.</returns>
    bool IsWithinRoot(string rootPath, string targetPath);

    /// <summary>
    /// Resolves a relative path against a root, rejecting traversal outside the root.
    /// </summary>
    /// <param name="rootPath">The root directory path.</param>
    /// <param name="relativePath">The relative path to resolve.</param>
    /// <returns>The fully-qualified path inside the root.</returns>
    /// <exception cref="ArgumentException">
    /// The relative path is blank or escapes the root directory.
    /// </exception>
    string ResolveWithinRoot(string rootPath, string relativePath);
}
