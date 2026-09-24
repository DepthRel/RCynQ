using System.Text;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Hashing;

namespace RCynQ.Git.Manifest;

/// <summary>
/// Scans directories and compares them to produce file change manifests.
/// </summary>
/// <remarks>
/// Symbolic links are never followed; a link is described by the SHA-256 of its target text. Hidden files are included.
/// </remarks>
public sealed class DefaultFileManifestBuilder : IFileManifestBuilder
{
    /// <summary>
    /// The snapshot filter.
    /// </summary>
    private readonly SnapshotFilter _filter;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultFileManifestBuilder"/> class.
    /// </summary>
    /// <param name="filter">The snapshot filter.</param>
    public DefaultFileManifestBuilder(SnapshotFilter filter)
    {
        _filter = filter;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, CancellationToken cancellationToken = default)
        => ScanAsync(directory, directory, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, string ignoreRulesDirectory, CancellationToken cancellationToken = default)
    {
        var root = Guard.NotBlank(directory, nameof(directory));
        var rulesRoot = Guard.NotBlank(ignoreRulesDirectory, nameof(ignoreRulesDirectory));
        if (!Directory.Exists(root))
        {
            return Array.Empty<FileEntry>();
        }

        var rootFull = Path.GetFullPath(root);
        var rulesFull = Path.GetFullPath(rulesRoot);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        var entries = new List<FileEntry>();
        var pending = new Stack<(DirectoryInfo Directory, string Relative, IReadOnlyList<ScopedRules> Rules)>();
        pending.Push((new DirectoryInfo(rootFull), string.Empty, Extend(Array.Empty<ScopedRules>(), rulesFull, string.Empty)));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (current, currentRelative, rules) = pending.Pop();
            foreach (var info in current.EnumerateFileSystemInfos("*", options))
            {
                var relative = Path.GetRelativePath(rootFull, info.FullName).Replace('\\', '/');
                if (info is DirectoryInfo subdirectory && _filter.IsDirectoryExcluded(subdirectory.Name))
                {
                    continue;
                }

                if (info is FileInfo file && _filter.IsFileExcluded(file.Name))
                {
                    continue;
                }

                // A symbolic link to a directory is a file for Git, so a "name/" pattern does not describe it.
                var isDirectory = info is DirectoryInfo && info.LinkTarget is null;
                if (IsIgnored(rules, relative, isDirectory))
                {
                    continue;
                }

                if (info.LinkTarget is { } target)
                {
                    var targetBytes = Encoding.UTF8.GetBytes(target);
                    entries.Add(new FileEntry(relative, DefaultStateHasher.Sha256Hex(targetBytes), targetBytes.Length, true));
                    continue;
                }

                if (info is DirectoryInfo directoryInfo)
                {
                    pending.Push((directoryInfo, relative, Extend(rules, rulesFull, relative)));
                    continue;
                }

                var fileInfo = (FileInfo)info;
                entries.Add(new FileEntry(relative, await DefaultStateHasher.Sha256FileAsync(fileInfo.FullName, cancellationToken), fileInfo.Length, false));
            }
        }

        return entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<FileChange>> BuildAsync(string baseDirectory, string incomingDirectory, CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(baseDirectory, nameof(baseDirectory));
        Guard.NotBlank(incomingDirectory, nameof(incomingDirectory));

        // Both sides are read with the rules of the incoming tree: a line added to a .gitignore hides the files from the
        // base as well, instead of making them look deleted.
        var baseEntries = await ScanAsync(baseDirectory, incomingDirectory, cancellationToken);
        var incomingEntries = await ScanAsync(incomingDirectory, incomingDirectory, cancellationToken);
        return Compare(baseEntries, incomingEntries);
    }

    /// <summary>
    /// Adds the <c>.gitignore</c> of a directory to the rules that apply below it.
    /// </summary>
    /// <param name="parents">The rules of the parent directories, outermost first.</param>
    /// <param name="rulesRoot">The directory whose <c>.gitignore</c> files are read.</param>
    /// <param name="relativeDirectory">The directory relative to the scanned root, with <c>/</c> separators; empty for the root.</param>
    /// <returns>The rules that apply to the entries of the directory.</returns>
    private IReadOnlyList<ScopedRules> Extend(IReadOnlyList<ScopedRules> parents, string rulesRoot, string relativeDirectory)
    {
        if (!_filter.RespectGitIgnore)
        {
            return parents;
        }

        var file = Path.Combine(rulesRoot, relativeDirectory.Replace('/', Path.DirectorySeparatorChar), ".gitignore");
        if (!File.Exists(file))
        {
            return parents;
        }

        var rules = GitIgnoreRules.Parse(File.ReadAllText(file), _filter.GitIgnoreIgnoreCase);
        return rules.IsEmpty ? parents : parents.Append(new ScopedRules(relativeDirectory, rules)).ToArray();
    }

    /// <summary>
    /// Evaluates the rules of every enclosing directory; a deeper <c>.gitignore</c> overrides a shallower one.
    /// </summary>
    /// <param name="rules">The applicable rules, outermost first.</param>
    /// <param name="relativePath">The path relative to the scanned root.</param>
    /// <param name="isDirectory">Whether the path is a directory.</param>
    /// <returns><see langword="true"/> when the path is ignored.</returns>
    private static bool IsIgnored(IReadOnlyList<ScopedRules> rules, string relativePath, bool isDirectory)
    {
        for (var i = rules.Count - 1; i >= 0; i--)
        {
            var scoped = rules[i];
            var local = scoped.Directory.Length == 0 ? relativePath : relativePath[(scoped.Directory.Length + 1)..];
            if (scoped.Rules.Match(local, isDirectory) is { } ignored)
            {
                return ignored;
            }
        }

        return false;
    }

    /// <summary>
    /// The rules of one <c>.gitignore</c> file and the directory they are relative to.
    /// </summary>
    /// <param name="Directory">The directory of the file relative to the scanned root; empty for the root.</param>
    /// <param name="Rules">The rules.</param>
    private sealed record ScopedRules(string Directory, GitIgnoreRules Rules);

    /// <summary>
    /// Compares two scanned states.
    /// </summary>
    /// <param name="baseEntries">The base state.</param>
    /// <param name="incomingEntries">The incoming state.</param>
    /// <returns>The file changes ordered by path (ordinal); modified and deleted files carry their hash in the base state.</returns>
    public static IReadOnlyList<FileChange> Compare(IReadOnlyList<FileEntry> baseEntries, IReadOnlyList<FileEntry> incomingEntries)
    {
        ArgumentNullException.ThrowIfNull(baseEntries);
        ArgumentNullException.ThrowIfNull(incomingEntries);
        var baseByPath = baseEntries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var incomingByPath = incomingEntries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var changes = new List<FileChange>();

        foreach (var incoming in incomingEntries)
        {
            if (!baseByPath.TryGetValue(incoming.Path, out var existing))
            {
                changes.Add(new FileChange(ChangeKind.Added, incoming.Path, incoming.Sha256, incoming.SizeBytes, incoming.IsSymlink));
            }
            else if (existing.Sha256 != incoming.Sha256 || existing.IsSymlink != incoming.IsSymlink)
            {
                changes.Add(new FileChange(ChangeKind.Modified, incoming.Path, incoming.Sha256, incoming.SizeBytes, incoming.IsSymlink, existing.Sha256));
            }
        }

        foreach (var removed in baseEntries.Where(entry => !incomingByPath.ContainsKey(entry.Path)))
        {
            changes.Add(new FileChange(ChangeKind.Deleted, removed.Path, null, null, removed.IsSymlink, removed.Sha256));
        }

        return changes.OrderBy(change => change.Path, StringComparer.Ordinal).ToArray();
    }
}
