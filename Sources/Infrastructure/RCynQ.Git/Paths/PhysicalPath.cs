namespace RCynQ.Git.Paths;

/// <summary>
/// Resolves paths the way the file system does: every symbolic link or junction on the way is followed before the
/// next segment, so <c>..</c> after a link leaves the directory the link points to, not the one it lies in.
/// </summary>
/// <remarks>
/// <see cref="Path.GetFullPath(string)"/> removes <c>..</c> by the text alone. For <c>w → d/u/..</c> with
/// <c>d/u → ..</c> it answers "inside the tree", while the file system takes <c>w</c> one level above the tree. A check
/// that must hold on the disk therefore asks this class.
/// </remarks>
public static class PhysicalPath
{
    /// <summary>
    /// The most links followed for one path; a longer chain is treated as a loop, the way the operating system does.
    /// </summary>
    public const int MAX_LINK_HOPS = 40;

    /// <summary>
    /// Gets the target of a symbolic link or junction as it is stored, also when the target does not exist.
    /// </summary>
    /// <param name="path">The path to look at.</param>
    /// <returns>The stored target, or <see langword="null"/> when the path is no link or does not exist.</returns>
    public static string? LinkTargetOf(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return new FileInfo(path).LinkTarget ?? new DirectoryInfo(path).LinkTarget;
    }

    /// <summary>
    /// Resolves a fully qualified path, following every link on the way, including the last segment.
    /// </summary>
    /// <param name="path">A fully qualified path; segments that do not exist are taken as they are.</param>
    /// <returns>The resolved path, or <see langword="null"/> when more than <see cref="MAX_LINK_HOPS"/> links are on the way.</returns>
    /// <exception cref="ArgumentException">The path is not fully qualified.</exception>
    public static string? Resolve(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"'{path}' is not a fully qualified path.", nameof(path));
        }

        var current = Path.GetPathRoot(path)!;
        var pending = new Stack<string>();
        Push(pending, path[current.Length..]);
        var hops = 0;

        while (pending.Count > 0)
        {
            var segment = pending.Pop();
            if (segment is "" or ".")
            {
                continue;
            }

            if (segment == "..")
            {
                current = Path.GetDirectoryName(current) ?? current;
                continue;
            }

            var next = Path.Join(current, segment);
            var target = LinkTargetOf(next);
            if (target is null)
            {
                current = next;
                continue;
            }

            if (++hops > MAX_LINK_HOPS)
            {
                return null;
            }

            // The target is read from where the link lies: an absolute one starts over at its own root.
            if (Path.IsPathFullyQualified(target))
            {
                current = Path.GetPathRoot(target)!;
                target = target[current.Length..];
            }

            Push(pending, target);
        }

        return Path.TrimEndingDirectorySeparator(current);
    }

    /// <summary>
    /// Puts the segments of a relative path on the stack so that the first segment is taken first.
    /// </summary>
    /// <param name="pending">The segments still to resolve.</param>
    /// <param name="relative">The relative path.</param>
    private static void Push(Stack<string> pending, string relative)
    {
        // On Unix a backslash is part of a name, so only the separators of the platform split.
        var segments = relative.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
        for (var index = segments.Length - 1; index >= 0; index--)
        {
            pending.Push(segments[index]);
        }
    }
}
