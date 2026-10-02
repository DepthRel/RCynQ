using System.Diagnostics;
using System.Text;
using FluentAssertions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Git.Paths;
using RCynQ.Sync.Apply;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="ContentOverwriteApplier"/> for entries that are links to a directory.
/// </summary>
/// <remarks>
/// Such a link is one manifest entry, like a file. The file system, however, sees a directory, and on Windows a link to a
/// directory cannot be removed as a file. The tests are skipped on a machine where no link can be created.
/// </remarks>
public sealed class ContentOverwriteApplierLinkTests : IDisposable
{
    /// <summary>The temporary root.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-applier-links", Guid.NewGuid().ToString("N"));

    /// <summary>The tree the patch is applied to.</summary>
    private readonly string _tree;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOverwriteApplierLinkTests"/> class.
    /// </summary>
    public ContentOverwriteApplierLinkTests()
    {
        _tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(_tree, "real"));
        File.WriteAllText(Path.Combine(_tree, "real", "kept.txt"), "kept\n");
    }

    /// <summary>
    /// Verifies that deleting a link to a directory removes the link and leaves the directory it points to untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Deleting_A_Link_To_A_Directory_Removes_Only_The_Link()
    {
        var link = Path.Combine(_tree, "link");
        if (!TryCreateDirectoryLink(link, Path.Combine(_tree, "real")))
        {
            return;
        }

        var result = await Apply(new FileChange(ChangeKind.Deleted, "link", null, null, true, new string('a', 64)), string.Empty);

        result.Status.Should().Be(PatchStatus.Applied);
        Directory.Exists(link).Should().BeFalse("the link is gone");
        File.Exists(Path.Combine(_tree, "real", "kept.txt")).Should().BeTrue("the directory behind the link is not part of the patch");
    }

    /// <summary>
    /// Verifies that a file that replaces a link to a directory is written in place of the link.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_Replaces_A_Link_To_A_Directory()
    {
        var link = Path.Combine(_tree, "link");
        if (!TryCreateDirectoryLink(link, Path.Combine(_tree, "real")))
        {
            return;
        }

        const string Content = "now a file\n";
        var bytes = Encoding.UTF8.GetBytes(Content);

        var result = await Apply(new FileChange(ChangeKind.Modified, "link", DefaultStateHasher.Sha256Hex(bytes), bytes.Length, false, new string('a', 64)), Content);

        result.Status.Should().Be(PatchStatus.Applied);
        File.ReadAllText(link).Should().Be(Content);
        File.Exists(Path.Combine(_tree, "real", "kept.txt")).Should().BeTrue();
    }

    /// <summary>
    /// Deletes the temporary directories.
    /// </summary>
    public void Dispose()
    {
        try
        {
            // A link that is still there is removed first, so the cleanup never follows it.
            var link = Path.Combine(_tree, "link");
            if (new DirectoryInfo(link) is { Exists: true, LinkTarget: not null })
            {
                Directory.Delete(link);
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Applies a patch with one change to the tree.
    /// </summary>
    /// <param name="change">The change.</param>
    /// <param name="content">The content served for the change.</param>
    /// <returns>The result.</returns>
    private Task<PatchApplyResult> Apply(FileChange change, string content)
    {
        var hash = new string('b', 64);
        var envelope = new PatchEnvelope(Guid.NewGuid(), new NodeId("source-1"), new NodeId("dest-1"), DateTimeOffset.UtcNow, hash, hash, new[] { change });
        return new ContentOverwriteApplier(TestTexts.English, new PathUtility(TestTexts.English)).ApplyAsync(envelope, new TextContent(content), _tree);
    }

    /// <summary>
    /// Creates a link to a directory: a symbolic link, or on Windows without the privilege for one, a junction.
    /// </summary>
    /// <param name="link">The link to create.</param>
    /// <param name="target">The directory to point to.</param>
    /// <returns><see langword="true"/> when the link exists.</returns>
    private static bool TryCreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }

            var info = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
            {
                info.ArgumentList.Add(argument);
            }

            using var process = Process.Start(info);
            process?.WaitForExit(10_000);
        }

        return new DirectoryInfo(link) is { Exists: true, LinkTarget: not null };
    }

    /// <summary>
    /// Patch content that serves the same text for every change.
    /// </summary>
    private sealed class TextContent : IPatchContent
    {
        /// <summary>The served text.</summary>
        private readonly string _text;

        /// <summary>
        /// Initializes a new instance of the <see cref="TextContent"/> class.
        /// </summary>
        /// <param name="text">The served text.</param>
        public TextContent(string text) => _text = text;

        /// <inheritdoc />
        public Task<Stream> OpenReadAsync(FileChange change, CancellationToken cancellationToken = default)
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(_text)));
    }
}
