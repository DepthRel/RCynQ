using System.Text;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Patches.Diff;

/// <summary>
/// Default <see cref="IPatchDiffBuilder"/>: stages the old and the new side of every changed file and runs <see cref="IGitDiffBuilder"/>.
/// </summary>
/// <remarks>
/// With size limits, files above the limits are not diffed; a comment line at the top of the diff names each of them, so
/// a display diff stays small even when a patch carries a whole project. A diff built with
/// <see cref="PatchDiffLimits.Full"/> never leaves a file out.
/// </remarks>
public sealed class PatchDiffBuilder : IPatchDiffBuilder
{
    /// <summary>
    /// The diff builder.
    /// </summary>
    private readonly IGitDiffBuilder _diffBuilder;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The node data directories, or <see langword="null"/> to stage in the system temporary directory.
    /// </summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchDiffBuilder"/> class.
    /// </summary>
    /// <param name="diffBuilder">The diff builder.</param>
    /// <param name="paths">The path utility.</param>
    /// <param name="appData">The node data directories; the staging area is created in its <c>tmp</c> directory.</param>
    public PatchDiffBuilder(IGitDiffBuilder diffBuilder, IPathUtility paths, IAppDataFactory? appData = null)
    {
        _diffBuilder = diffBuilder;
        _paths = paths;
        _appData = appData;
    }

    /// <inheritdoc />
    public async Task<byte[]> BuildAsync(
        IReadOnlyList<FileChange> manifest,
        string baseDirectory,
        IPatchContent content,
        PatchDiffLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(baseDirectory);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(limits);
        if (manifest.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var tempRoot = _appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq");
        var staging = Path.Combine(tempRoot, "stage-" + Guid.NewGuid().ToString("N"));
        var oldSide = Path.Combine(staging, "a");
        var newSide = Path.Combine(staging, "b");
        Directory.CreateDirectory(oldSide);
        Directory.CreateDirectory(newSide);
        try
        {
            var skipped = new StringBuilder();
            long total = 0;
            foreach (var change in manifest)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var oldFile = change.Kind is ChangeKind.Modified or ChangeKind.Deleted ? _paths.ResolveWithinRoot(baseDirectory, change.Path) : null;
                var size = Math.Max(change.SizeBytes ?? 0, oldFile is not null && File.Exists(oldFile) ? new FileInfo(oldFile).Length : 0);
                if (limits.MaxFileBytes is { } maxFile && size > maxFile ||
                    limits.MaxTotalBytes is { } maxTotal && total + size > maxTotal)
                {
                    skipped.Append("# rcynq: ").Append(change.Path).Append(" (").Append(size).Append(" bytes) is not shown in this diff.\n");
                    continue;
                }

                total += size;
                if (oldFile is not null && (File.Exists(oldFile) || new FileInfo(oldFile).LinkTarget is not null))
                {
                    var info = new FileInfo(oldFile);
                    var oldTarget = _paths.ResolveWithinRoot(oldSide, change.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(oldTarget)!);
                    if (info.LinkTarget is { } link)
                    {
                        await File.WriteAllBytesAsync(oldTarget, Encoding.UTF8.GetBytes(link), cancellationToken);
                    }
                    else
                    {
                        File.Copy(oldFile, oldTarget, overwrite: true);
                    }
                }

                if (change.Kind is ChangeKind.Added or ChangeKind.Modified)
                {
                    var newTarget = _paths.ResolveWithinRoot(newSide, change.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(newTarget)!);
                    await using var source = await content.OpenReadAsync(change, cancellationToken);
                    await using var destination = File.Create(newTarget);
                    await source.CopyToAsync(destination, cancellationToken);
                }
            }

            var diff = await _diffBuilder.BuildAsync(staging, cancellationToken);
            return skipped.Length == 0 ? diff : Encoding.UTF8.GetBytes(skipped.ToString()).Concat(diff).ToArray();
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Deletes a temporary directory, ignoring failures.
    /// </summary>
    /// <param name="path">The directory.</param>
    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Temporary staging data; best effort cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // Temporary staging data; best effort cleanup.
        }
    }
}
