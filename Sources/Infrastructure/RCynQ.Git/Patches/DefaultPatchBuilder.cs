using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.Validation;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Git.Patches.Diff;

namespace RCynQ.Git.Patches;

/// <summary>
/// Builds complete patch envelopes: manifest, state hashes, file bundle and unified binary diff.
/// </summary>
/// <remarks>
/// The bundle is built first and its verified bytes are used for the <c>b</c> side of the diff staging area, so the diff
/// and the bundle always describe the same content even when the working tree is edited during the build.
/// </remarks>
public sealed class DefaultPatchBuilder : IPatchBuilder
{
    /// <summary>
    /// The manifest builder.
    /// </summary>
    private readonly IFileManifestBuilder _manifestBuilder;

    /// <summary>
    /// The state hasher.
    /// </summary>
    private readonly IStateHasher _hasher;

    /// <summary>
    /// The diff builder.
    /// </summary>
    private readonly IGitDiffBuilder _diffBuilder;

    /// <summary>
    /// The bundle builder.
    /// </summary>
    private readonly IFileBundleBuilder _bundleBuilder;

    /// <summary>
    /// The path utility.
    /// </summary>
    private readonly IPathUtility _paths;

    /// <summary>
    /// The clock.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// The node data directories, or <see langword="null"/> to stage in the system temporary directory.
    /// </summary>
    private readonly IAppDataFactory? _appData;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPatchBuilder"/> class.
    /// </summary>
    /// <param name="manifestBuilder">The manifest builder.</param>
    /// <param name="hasher">The state hasher.</param>
    /// <param name="diffBuilder">The diff builder.</param>
    /// <param name="bundleBuilder">The bundle builder.</param>
    /// <param name="paths">The path utility.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="appData">The node data directories; the diff staging area is created in its <c>tmp</c> directory.</param>
    public DefaultPatchBuilder(
        IFileManifestBuilder manifestBuilder,
        IStateHasher hasher,
        IGitDiffBuilder diffBuilder,
        IFileBundleBuilder bundleBuilder,
        IPathUtility paths,
        TimeProvider timeProvider,
        IAppDataFactory? appData = null)
    {
        _manifestBuilder = manifestBuilder;
        _hasher = hasher;
        _diffBuilder = diffBuilder;
        _bundleBuilder = bundleBuilder;
        _paths = paths;
        _timeProvider = timeProvider;
        _appData = appData;
    }

    /// <inheritdoc />
    public async Task<PatchEnvelope> BuildAsync(
        NodeId sourceNode,
        NodeId targetNode,
        string baseDirectory,
        string incomingDirectory,
        CancellationToken cancellationToken = default)
    {
        Guard.NotBlank(baseDirectory, nameof(baseDirectory));
        Guard.NotBlank(incomingDirectory, nameof(incomingDirectory));

        var baseEntries = await _manifestBuilder.ScanAsync(baseDirectory, cancellationToken);
        var incomingEntries = await _manifestBuilder.ScanAsync(incomingDirectory, cancellationToken);
        var manifest = DefaultFileManifestBuilder.Compare(baseEntries, incomingEntries);

        var bundleBytes = await _bundleBuilder.BuildAsync(manifest, incomingDirectory, cancellationToken);
        var diff = manifest.Count == 0
            ? Array.Empty<byte>()
            : await BuildDiffAsync(manifest, baseDirectory, _bundleBuilder.Read(bundleBytes, manifest), cancellationToken);

        return new PatchEnvelope(
            Guid.NewGuid(),
            sourceNode,
            targetNode,
            _timeProvider.GetUtcNow(),
            _hasher.ComputeHash(baseEntries),
            _hasher.ComputeHash(incomingEntries),
            manifest,
            diff,
            bundleBytes);
    }

    /// <summary>
    /// Stages the changed files and runs the diff builder.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <param name="baseDirectory">The base directory.</param>
    /// <param name="bundle">The verified bundle with incoming content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The diff bytes.</returns>
    private async Task<byte[]> BuildDiffAsync(IReadOnlyList<FileChange> manifest, string baseDirectory, FileBundle bundle, CancellationToken cancellationToken)
    {
        var tempRoot = _appData?.Create().TempDirectory ?? Path.Combine(Path.GetTempPath(), "rcynq");
        var staging = Path.Combine(tempRoot, "stage-" + Guid.NewGuid().ToString("N"));
        var oldSide = Path.Combine(staging, "a");
        var newSide = Path.Combine(staging, "b");
        Directory.CreateDirectory(oldSide);
        Directory.CreateDirectory(newSide);
        try
        {
            foreach (var change in manifest)
            {
                if (change.Kind is ChangeKind.Modified or ChangeKind.Deleted)
                {
                    var source = _paths.ResolveWithinRoot(baseDirectory, change.Path);
                    var info = new FileInfo(source);
                    var content = info.LinkTarget is { } link
                        ? System.Text.Encoding.UTF8.GetBytes(link)
                        : await File.ReadAllBytesAsync(source, cancellationToken);
                    await WriteAsync(_paths.ResolveWithinRoot(oldSide, change.Path), content, cancellationToken);
                }

                if (change.Kind is ChangeKind.Added or ChangeKind.Modified)
                {
                    var content = change.IsSymlink
                        ? System.Text.Encoding.UTF8.GetBytes(bundle.Symlinks[change.Path])
                        : bundle.Files[change.Path];
                    await WriteAsync(_paths.ResolveWithinRoot(newSide, change.Path), content, cancellationToken);
                }
            }

            return await _diffBuilder.BuildAsync(staging, cancellationToken);
        }
        finally
        {
            TryDelete(staging);
        }
    }

    /// <summary>
    /// Writes a staged file, creating parent directories.
    /// </summary>
    /// <param name="path">The target path.</param>
    /// <param name="content">The content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    private static async Task WriteAsync(string path, byte[] content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, cancellationToken);
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
