using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Paths;
using RCynQ.Localization;

namespace RCynQ.Sync.Apply;

/// <summary>
/// <see cref="IPatchApplier"/> that writes the complete incoming content of every file over the target directory.
/// </summary>
/// <remarks>
/// Content is streamed from the source to a temporary file, so a large file is never held in memory. Each file is replaced atomically; writing below a symbolic link or
/// creating a symbolic link that points outside the target directory is refused. Unix file modes of replaced files are kept.
/// </remarks>
public sealed class ContentOverwriteApplier : IPatchApplier
{
    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOverwriteApplier"/> class.
    /// </summary>
    /// <param name="paths">The path utility.</param>
    public ContentOverwriteApplier(IPathUtility paths)
    {
        _paths = paths;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ApplyAsync(PatchEnvelope envelope, IPatchContent content, string targetDirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(content);
        var root = Path.GetFullPath(Guard.NotBlank(targetDirectory, nameof(targetDirectory)));
        Directory.CreateDirectory(root);
        var count = 0;
        foreach (var change in envelope.Manifest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var full = _paths.ResolveWithinRoot(root, change.Path);
            if (PathUtility.HasLinkedParent(root, full))
            {
                throw new PatchApplyException($"'{change.Path}' is below a symbolic link; refusing to write through it.");
            }

            try
            {
                if (change.Kind == ChangeKind.Deleted)
                {
                    DeleteEntry(full);
                    RemoveEmptyParents(root, full);
                }
                else if (change.IsSymlink)
                {
                    await using var link = await content.OpenReadAsync(change, cancellationToken);
                    using var reader = new StreamReader(link, System.Text.Encoding.UTF8);
                    WriteSymlink(root, full, await reader.ReadToEndAsync(cancellationToken));
                }
                else
                {
                    await using var source = await content.OpenReadAsync(change, cancellationToken);
                    await WriteFileAsync(full, source, cancellationToken);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new PatchApplyException(Texts.Format("Apply.FileFailed", change.Path, ex.Message), ex);
            }

            count++;
        }

        return PatchApplyResult.Applied(envelope.Id, count);
    }

    /// <summary>
    /// Deletes a file or symbolic link; refuses to delete a real directory.
    /// </summary>
    /// <param name="full">The full path.</param>
    private static void DeleteEntry(string full)
    {
        var file = new FileInfo(full);
        if (file.LinkTarget is not null || file.Exists)
        {
            File.Delete(full);
            return;
        }

        var directory = new DirectoryInfo(full);
        if (directory.Exists)
        {
            if (directory.LinkTarget is null)
            {
                throw new IOException(Texts.Get("Apply.DirectoryInPlaceOfFile"));
            }

            Directory.Delete(full);
        }
    }

    /// <summary>
    /// Writes a regular file through a temporary file.
    /// </summary>
    /// <param name="full">The full path.</param>
    /// <param name="content">The content stream.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is in place.</returns>
    private static async Task WriteFileAsync(string full, Stream content, CancellationToken cancellationToken)
    {
        if (Directory.Exists(full) && new DirectoryInfo(full).LinkTarget is null)
        {
            throw new IOException(Texts.Get("Apply.DirectoryInPlaceOfFileTarget"));
        }

        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(full)}.rcynq-{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var destination = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await content.CopyToAsync(destination, cancellationToken);
            }

            var existing = new FileInfo(full);
            if (existing.LinkTarget is not null)
            {
                File.Delete(full);
            }
            else if (existing.Exists && !OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
            }

            File.Move(temp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Creates a symbolic link whose target stays inside the root.
    /// </summary>
    /// <param name="root">The root directory.</param>
    /// <param name="full">The link path.</param>
    /// <param name="target">The link target.</param>
    /// <exception cref="PatchApplyException">The target is absolute or escapes the root.</exception>
    private void WriteSymlink(string root, string full, string target)
    {
        var resolved = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(full)!, target));
        if (Path.IsPathRooted(target) || !_paths.IsWithinRoot(root, resolved))
        {
            throw new PatchApplyException(Texts.Format("Apply.SymlinkOutside", full, target));
        }

        DeleteEntry(full);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.CreateSymbolicLink(full, target);
    }

    /// <summary>
    /// Removes empty parent directories up to the root.
    /// </summary>
    /// <param name="root">The root directory.</param>
    /// <param name="full">The deleted path.</param>
    private static void RemoveEmptyParents(string root, string full)
    {
        var current = Path.GetDirectoryName(full);
        var rootTrimmed = Path.TrimEndingDirectorySeparator(root);
        while (!string.IsNullOrEmpty(current)
            && current.Length > rootTrimmed.Length
            && Directory.Exists(current)
            && !Directory.EnumerateFileSystemEntries(current).Any())
        {
            Directory.Delete(current);
            current = Path.GetDirectoryName(current);
        }
    }
}
