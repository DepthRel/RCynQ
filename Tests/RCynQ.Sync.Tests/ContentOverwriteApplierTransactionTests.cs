using System.Text;
using FluentAssertions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Paths;
using RCynQ.Sync.Apply;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests that <see cref="ContentOverwriteApplier"/> applies a patch completely or not at all.
/// </summary>
public sealed class ContentOverwriteApplierTransactionTests : IDisposable
{
    /// <summary>The tree the patch is applied to.</summary>
    private readonly string _tree = Path.Combine(Path.GetTempPath(), "rcynq-applier-tx", Guid.NewGuid().ToString("N"));

    /// <summary>The content the patch carries, by path.</summary>
    private readonly Dictionary<string, string> _content = new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOverwriteApplierTransactionTests"/> class with a tree of
    /// <c>a.txt</c>, <c>b.txt</c> and the directory <c>taken</c>.
    /// </summary>
    public ContentOverwriteApplierTransactionTests()
    {
        Write("a.txt", "v1\n");
        Write("b.txt", "keep\n");
        Write("taken/inside.txt", "a directory, not a file\n");
    }

    /// <summary>
    /// Verifies that a patch whose last file cannot be put in place leaves the tree exactly as it was: the files written
    /// and deleted before it are restored, and the directories created for it are removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failure_While_Files_Are_Put_In_Place_Restores_The_Tree()
    {
        var before = await SnapshotAsync();
        var manifest = new[]
        {
            Modified("a.txt", "v2\n"),
            Deleted("b.txt"),
            Added("new/deep/n.txt", "new\n"),
            Added("taken", "a file where a directory is\n")
        };

        var apply = () => Apply(manifest);

        await apply.Should().ThrowAsync<PatchApplyException>();
        (await SnapshotAsync()).Should().Equal(before);
        Directory.Exists(Path.Combine(_tree, "new")).Should().BeFalse("directories created for the patch are removed with it");
        Directory.EnumerateDirectories(_tree, PathUtility.APPLY_STAGING_PREFIX + "*").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a patch whose content cannot be read touches nothing: every file is prepared before the first one
    /// is put in place.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failure_While_The_Patch_Is_Prepared_Touches_Nothing()
    {
        var before = await SnapshotAsync();
        var manifest = new[] { Modified("a.txt", "v2\n"), Deleted("b.txt"), Added("missing.txt", "never delivered\n") };
        _content.Remove("missing.txt");

        var apply = () => Apply(manifest);

        await apply.Should().ThrowAsync<PatchApplyException>();
        (await SnapshotAsync()).Should().Equal(before);
        Directory.EnumerateDirectories(_tree, PathUtility.APPLY_STAGING_PREFIX + "*").Should().BeEmpty();
    }

    /// <summary>
    /// Verifies a patch that succeeds: files are written, deleted and created below new directories, a file is replaced
    /// by a directory of the same name, and nothing of the preparation stays behind.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Successful_Patch_Is_Applied_Completely()
    {
        var manifest = new[]
        {
            Modified("a.txt", "v2\n"),
            Deleted("b.txt"),
            Added("b.txt/now-a-directory.txt", "inside\n"),
            Added("new/deep/n.txt", "new\n"),
            Deleted("taken/inside.txt")
        };

        var result = await Apply(manifest);

        result.Status.Should().Be(PatchStatus.Applied);
        result.AppliedFileCount.Should().Be(5);
        File.ReadAllText(Path.Combine(_tree, "a.txt")).Should().Be("v2\n");
        File.ReadAllText(Path.Combine(_tree, "b.txt", "now-a-directory.txt")).Should().Be("inside\n");
        File.ReadAllText(Path.Combine(_tree, "new", "deep", "n.txt")).Should().Be("new\n");
        Directory.Exists(Path.Combine(_tree, "taken")).Should().BeFalse("a directory left empty by a deletion is removed");
        Directory.EnumerateFileSystemEntries(_tree).Select(Path.GetFileName).Should().BeEquivalentTo("a.txt", "b.txt", "new");
    }

    /// <summary>
    /// Verifies that a staging directory left behind by a process that died is not part of a snapshot.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Leftover_Staging_Directory_Is_Not_Scanned()
    {
        Write(PathUtility.APPLY_STAGING_PREFIX + "0123/0.new", "half a patch\n");

        var entries = await new DefaultFileManifestBuilder(new SnapshotFilter()).ScanAsync(_tree);

        entries.Select(entry => entry.Path).Should().Equal("a.txt", "b.txt", "taken/inside.txt");
    }

    /// <summary>
    /// Deletes the tree.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_tree, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Applies a manifest to the tree.
    /// </summary>
    /// <param name="manifest">The manifest.</param>
    /// <returns>The result.</returns>
    private Task<PatchApplyResult> Apply(IReadOnlyList<FileChange> manifest)
    {
        var hash = new string('b', 64);
        var envelope = new PatchEnvelope(Guid.NewGuid(), new NodeId("workstation-1"), new NodeId("origin-1"), DateTimeOffset.UtcNow, hash, hash, manifest);
        return new ContentOverwriteApplier(TestTexts.English, new PathUtility(TestTexts.English)).ApplyAsync(envelope, new MapContent(_content), _tree);
    }

    /// <summary>
    /// Reads the tree as path and content pairs.
    /// </summary>
    /// <returns>The files of the tree.</returns>
    private async Task<IReadOnlyList<(string Path, string Sha256)>> SnapshotAsync()
        => (await new DefaultFileManifestBuilder(new SnapshotFilter()).ScanAsync(_tree)).Select(entry => (entry.Path, entry.Sha256)).ToArray();

    /// <summary>
    /// Writes a file of the tree.
    /// </summary>
    /// <param name="relative">The relative path.</param>
    /// <param name="text">The content.</param>
    private void Write(string relative, string text)
    {
        var full = Path.Combine(_tree, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text);
    }

    /// <summary>
    /// Creates a change that adds a file and registers its content.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="text">The content.</param>
    /// <returns>The change.</returns>
    private FileChange Added(string path, string text)
    {
        _content[path] = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FileChange(ChangeKind.Added, path, DefaultStateHasher.Sha256Hex(bytes), bytes.Length, false);
    }

    /// <summary>
    /// Creates a change that modifies a file and registers its content.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="text">The content.</param>
    /// <returns>The change.</returns>
    private FileChange Modified(string path, string text)
    {
        _content[path] = text;
        var bytes = Encoding.UTF8.GetBytes(text);
        return new FileChange(ChangeKind.Modified, path, DefaultStateHasher.Sha256Hex(bytes), bytes.Length, false, new string('a', 64));
    }

    /// <summary>
    /// Creates a change that deletes a file.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>The change.</returns>
    private static FileChange Deleted(string path) => new(ChangeKind.Deleted, path, null, null, false, new string('a', 64));

    /// <summary>
    /// Patch content served from a dictionary; a path that is not in it cannot be read.
    /// </summary>
    private sealed class MapContent : IPatchContent
    {
        /// <summary>The content by path.</summary>
        private readonly IReadOnlyDictionary<string, string> _content;

        /// <summary>
        /// Initializes a new instance of the <see cref="MapContent"/> class.
        /// </summary>
        /// <param name="content">The content by path.</param>
        public MapContent(IReadOnlyDictionary<string, string> content) => _content = content;

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
            => _content.TryGetValue(change.Path, out var text)
                ? Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(text)))
                : throw new FileNotFoundException($"No content for '{change.Path}'.");
    }
}
