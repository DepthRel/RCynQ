using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Paths;

namespace RCynQ.Git.Paths;

/// <summary>
/// Safe handling of working tree relative paths.
/// </summary>
/// <remarks>
/// <para>
/// Relative paths that arrive in patches are untrusted. A path is accepted only when it is relative, contains no
/// <c>..</c> segment, no drive or stream separator (<c>:</c>), no control characters and does not address Git
/// metadata. Git metadata is rejected in every spelling Windows would map to <c>.git</c> (any case, trailing dots or
/// spaces, 8.3 short name <c>git~N</c>), because writing into <c>.git/hooks</c> would allow code execution.
/// </para>
/// <para>
/// Normalized paths use <c>/</c> separators and no empty or <c>.</c> segments.
/// </para>
/// </remarks>
public sealed partial class PathUtility : IPathUtility
{
    /// <summary>
    /// The comparison used for file system paths on the current platform.
    /// </summary>
    public static readonly StringComparison PlatformComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    /// <inheritdoc />
    public string NormalizeRelativePath(string relativePath)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must not be blank.", nameof(relativePath));
        }

        if (relativePath.Any(char.IsControl))
        {
            throw new ArgumentException($"Relative path '{Printable(relativePath)}' contains control characters.", nameof(relativePath));
        }

        var unified = relativePath.Replace('\\', '/');
        if (unified.StartsWith('/') || unified.Contains(':') || Path.IsPathRooted(relativePath))
        {
            throw new ArgumentException($"Path '{relativePath}' must be relative.", nameof(relativePath));
        }

        var segments = new List<string>();
        foreach (var segment in unified.Split('/'))
        {
            if (segment.Length == 0 || segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                throw new ArgumentException($"Path '{relativePath}' escapes the working tree.", nameof(relativePath));
            }

            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                throw new ArgumentException($"Path '{relativePath}' has a segment ending with a dot or space.", nameof(relativePath));
            }

            if (IsGitMetadataName(segment))
            {
                throw new ArgumentException($"Path '{relativePath}' addresses Git metadata.", nameof(relativePath));
            }

            segments.Add(segment);
        }

        if (segments.Count == 0)
        {
            throw new ArgumentException($"Path '{relativePath}' does not name a file.", nameof(relativePath));
        }

        return string.Join('/', segments);
    }

    /// <inheritdoc />
    public bool IsWithinRoot(string rootPath, string targetPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(targetPath))
        {
            return false;
        }

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        return target.Equals(root, PlatformComparison)
            || target.StartsWith(root + Path.DirectorySeparatorChar, PlatformComparison);
    }

    /// <inheritdoc />
    public string ResolveWithinRoot(string rootPath, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new ArgumentException("Root path must not be blank.", nameof(rootPath));
        }

        var normalized = NormalizeRelativePath(relativePath);
        var root = Path.GetFullPath(rootPath);
        var full = Path.GetFullPath(Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        if (!IsWithinRoot(root, full) || Path.TrimEndingDirectorySeparator(full).Equals(Path.TrimEndingDirectorySeparator(root), PlatformComparison))
        {
            throw new ArgumentException($"Path '{relativePath}' escapes the root '{rootPath}'.", nameof(relativePath));
        }

        return full;
    }

    /// <summary>
    /// Checks whether any existing directory between the root and the target is a symbolic link or junction.
    /// </summary>
    /// <param name="rootPath">The root directory.</param>
    /// <param name="fullPath">A full path inside the root.</param>
    /// <returns><see langword="true"/> when writing to the path could be redirected outside the root.</returns>
    public static bool HasLinkedParent(string rootPath, string fullPath)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
        var current = Path.GetDirectoryName(Path.GetFullPath(fullPath));
        while (!string.IsNullOrEmpty(current) && current.Length > root.Length)
        {
            var info = new DirectoryInfo(current);
            if (info.Exists && info.LinkTarget is not null)
            {
                return true;
            }

            current = Path.GetDirectoryName(current);
        }

        return false;
    }

    /// <summary>
    /// Checks whether a path segment names Git metadata in any spelling.
    /// </summary>
    /// <param name="segment">The path segment.</param>
    /// <returns><see langword="true"/> for Git metadata names.</returns>
    public static bool IsGitMetadataName(string segment)
        => segment.Equals(".git", StringComparison.OrdinalIgnoreCase) || ShortGitName().IsMatch(segment);

    /// <summary>
    /// Replaces control characters for error messages.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The printable value.</returns>
    private static string Printable(string value) => new(value.Select(c => char.IsControl(c) ? '?' : c).ToArray());

    /// <summary>
    /// Matches the Windows 8.3 short name of <c>.git</c>.
    /// </summary>
    /// <returns>The compiled regular expression.</returns>
    [GeneratedRegex("^git~[0-9]+$", RegexOptions.IgnoreCase)]
    private static partial Regex ShortGitName();
}
