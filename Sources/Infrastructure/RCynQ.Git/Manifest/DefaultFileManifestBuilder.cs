using System.Text;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Hashing;
using RCynQ.Git.Paths;

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
    public async Task<IReadOnlyList<FileEntry>> ScanAsync(string directory, CancellationToken cancellationToken = default)
    {
        var root = Guard.NotBlank(directory, nameof(directory));
        if (!Directory.Exists(root))
        {
            return Array.Empty<FileEntry>();
        }

        var rootFull = Path.GetFullPath(root);
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false };
        var entries = new List<FileEntry>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(rootFull));

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var info in pending.Pop().EnumerateFileSystemInfos("*", options))
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

                if (info.LinkTarget is { } target)
                {
                    var targetBytes = Encoding.UTF8.GetBytes(target);
                    entries.Add(new FileEntry(relative, DefaultStateHasher.Sha256Hex(targetBytes), targetBytes.Length, true));
                    continue;
                }

                if (info is DirectoryInfo directoryInfo)
                {
                    pending.Push(directoryInfo);
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
        var baseEntries = await ScanAsync(baseDirectory, cancellationToken);
        var incomingEntries = await ScanAsync(incomingDirectory, cancellationToken);
        return Compare(baseEntries, incomingEntries);
    }

    /// <summary>
    /// Compares two scanned states.
    /// </summary>
    /// <param name="baseEntries">The base state.</param>
    /// <param name="incomingEntries">The incoming state.</param>
    /// <returns>The file changes ordered by path (ordinal).</returns>
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
                changes.Add(new FileChange(ChangeKind.Modified, incoming.Path, incoming.Sha256, incoming.SizeBytes, incoming.IsSymlink));
            }
        }

        foreach (var removed in baseEntries.Where(entry => !incomingByPath.ContainsKey(entry.Path)))
        {
            changes.Add(new FileChange(ChangeKind.Deleted, removed.Path, null, null, removed.IsSymlink));
        }

        return changes.OrderBy(change => change.Path, StringComparer.Ordinal).ToArray();
    }
}
