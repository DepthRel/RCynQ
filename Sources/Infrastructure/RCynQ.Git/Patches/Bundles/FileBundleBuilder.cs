using System.IO.Compression;
using System.Text;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Hashing;
using RCynQ.Localization;

namespace RCynQ.Git.Patches.Bundles;

/// <summary>
/// Default <see cref="IFileBundleBuilder"/>.
/// </summary>
/// <remarks>
/// Layout: <c>files/&lt;path&gt;</c> for regular files, <c>symlinks/&lt;path&gt;</c> with the UTF-8 link target, and empty
/// <c>deleted/&lt;path&gt;</c> markers. Entries are sorted and use a fixed timestamp. Reading rejects unsafe paths
/// (zip-slip, Git metadata) and bundles whose total uncompressed size exceeds <see cref="MaxTotalBytes"/>.
/// </remarks>
public sealed class FileBundleBuilder : IFileBundleBuilder
{
    /// <summary>
    /// Maximum accepted total uncompressed size of a bundle.
    /// </summary>
    public const long MaxTotalBytes = 1L << 31;

    /// <summary>
    /// Prefix of regular file entries.
    /// </summary>
    private const string FilesPrefix = "files/";

    /// <summary>
    /// Prefix of symbolic link entries.
    /// </summary>
    private const string SymlinksPrefix = "symlinks/";

    /// <summary>
    /// Prefix of deletion markers.
    /// </summary>
    private const string DeletedPrefix = "deleted/";

    /// <summary>
    /// Fixed entry timestamp for reproducible archives.
    /// </summary>
    private static readonly DateTimeOffset EntryTimestamp = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="FileBundleBuilder"/> class.
    /// </summary>
    /// <param name="paths">The path utility.</param>
    public FileBundleBuilder(IPathUtility paths)
    {
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<byte[]> BuildAsync(IReadOnlyList<FileChange> changes, string incomingDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        var root = Guard.NotBlank(incomingDirectory, nameof(incomingDirectory));
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var change in changes.OrderBy(change => change.Path, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = _paths.NormalizeRelativePath(change.Path);
                switch (change.Kind)
                {
                    case ChangeKind.Deleted:
                        CreateEntry(zip, DeletedPrefix + path, Array.Empty<byte>());
                        break;
                    case ChangeKind.Added or ChangeKind.Modified:
                        var source = _paths.ResolveWithinRoot(root, path);
                        byte[] content;
                        if (change.IsSymlink)
                        {
                            var target = new FileInfo(source).LinkTarget ?? new DirectoryInfo(source).LinkTarget
                                ?? throw new SyncBridgeException($"'{path}' is no longer a symbolic link; rebuild the patch.");
                            content = Encoding.UTF8.GetBytes(target);
                        }
                        else
                        {
                            content = await File.ReadAllBytesAsync(source, cancellationToken);
                        }

                        if (DefaultStateHasher.Sha256Hex(content) != change.Sha256)
                        {
                            throw new SyncBridgeException($"'{path}' changed while the patch was built; rebuild the patch.");
                        }

                        CreateEntry(zip, (change.IsSymlink ? SymlinksPrefix : FilesPrefix) + path, content);
                        break;
                    default:
                        throw new ArgumentException($"Change kind {change.Kind} is not supported in bundles.", nameof(changes));
                }
            }
        }

        return buffer.ToArray();
    }

    /// <inheritdoc />
    public FileBundle Read(byte[] bundle, IReadOnlyList<FileChange>? expectedManifest = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var symlinks = new Dictionary<string, string>(StringComparer.Ordinal);
        var deleted = new HashSet<string>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        long total = 0;

        try
        {
            using var zip = new ZipArchive(new MemoryStream(bundle, writable: false), ZipArchiveMode.Read);
            foreach (var entry in zip.Entries)
            {
                total += entry.Length;
                if (total > MaxTotalBytes)
                {
                    throw new FormatException(Texts.Get("Bundle.TooBig"));
                }

                var (prefix, rawPath) = SplitEntryName(entry.FullName);
                string path;
                try
                {
                    path = _paths.NormalizeRelativePath(rawPath);
                }
                catch (ArgumentException ex)
                {
                    throw new FormatException(Texts.Format("Bundle.UnsafePath", entry.FullName, ex.Message), ex);
                }

                if (path != rawPath || !seen.Add(path))
                {
                    throw new FormatException(Texts.Format("Bundle.NotCanonical", entry.FullName));
                }

                switch (prefix)
                {
                    case DeletedPrefix:
                        deleted.Add(path);
                        break;
                    case FilesPrefix:
                        files[path] = ReadEntry(entry);
                        break;
                    default:
                        symlinks[path] = Encoding.UTF8.GetString(ReadEntry(entry));
                        break;
                }
            }
        }
        catch (InvalidDataException ex)
        {
            throw new FormatException(Texts.Format("Bundle.NotZip", ex.Message), ex);
        }

        var result = new FileBundle(files, symlinks, deleted);
        if (expectedManifest is not null)
        {
            Verify(result, expectedManifest);
        }

        return result;
    }

    /// <summary>
    /// Verifies that a bundle contains exactly the manifest content.
    /// </summary>
    /// <param name="bundle">The bundle.</param>
    /// <param name="manifest">The manifest.</param>
    /// <exception cref="FormatException">The bundle does not match.</exception>
    private static void Verify(FileBundle bundle, IReadOnlyList<FileChange> manifest)
    {
        if (bundle.Count != manifest.Count)
        {
            throw new FormatException(Texts.Format("Bundle.CountMismatch", bundle.Count, manifest.Count));
        }

        foreach (var change in manifest)
        {
            switch (change.Kind)
            {
                case ChangeKind.Deleted when bundle.Deleted.Contains(change.Path):
                    break;
                case ChangeKind.Added or ChangeKind.Modified when !change.IsSymlink && bundle.Files.TryGetValue(change.Path, out var content)
                    && DefaultStateHasher.Sha256Hex(content) == change.Sha256:
                    break;
                case ChangeKind.Added or ChangeKind.Modified when change.IsSymlink && bundle.Symlinks.TryGetValue(change.Path, out var target)
                    && DefaultStateHasher.Sha256Hex(Encoding.UTF8.GetBytes(target)) == change.Sha256:
                    break;
                default:
                    throw new FormatException(Texts.Format("Bundle.ContentMismatch", change.Path, change.Kind));
            }
        }
    }

    /// <summary>
    /// Splits an entry name into its prefix and relative path.
    /// </summary>
    /// <param name="fullName">The entry name.</param>
    /// <returns>The prefix and path.</returns>
    /// <exception cref="FormatException">The prefix is unknown.</exception>
    private static (string Prefix, string Path) SplitEntryName(string fullName)
    {
        foreach (var prefix in new[] { FilesPrefix, SymlinksPrefix, DeletedPrefix })
        {
            if (fullName.StartsWith(prefix, StringComparison.Ordinal))
            {
                return (prefix, fullName[prefix.Length..]);
            }
        }

        throw new FormatException(Texts.Format("Bundle.UnknownPrefix", fullName));
    }

    /// <summary>
    /// Reads the content of an entry.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The content.</returns>
    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    /// <summary>
    /// Adds an entry with the fixed timestamp.
    /// </summary>
    /// <param name="zip">The archive.</param>
    /// <param name="name">The entry name.</param>
    /// <param name="content">The content.</param>
    private static void CreateEntry(ZipArchive zip, string name, byte[] content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        entry.LastWriteTime = EntryTimestamp;
        using var stream = entry.Open();
        stream.Write(content);
    }
}
