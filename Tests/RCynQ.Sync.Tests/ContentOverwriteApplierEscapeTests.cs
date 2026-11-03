using System.Text;
using FluentAssertions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Hashing;
using RCynQ.Git.Paths;
using RCynQ.Sync.Apply;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="ContentOverwriteApplier"/> against patches whose symbolic links try to lead a write out of the tree
/// or into Git metadata.
/// </summary>
/// <remarks>
/// A link the patch creates is only on the disk while the patch is put in place, so a check of the tree as it was before
/// does not see it. Where this process may not create symbolic links (Windows without the privilege), creating the first
/// link fails already; the tests then still require that the patch is refused and leaves the tree as it was.
/// </remarks>
public sealed class ContentOverwriteApplierEscapeTests : IDisposable
{
    /// <summary>The temporary root; the tree lies one level below it, so a write one level up lands here.</summary>
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rcynq-applier-escape", Guid.NewGuid().ToString("N"));

    /// <summary>The tree the patch is applied to.</summary>
    private readonly string _tree;

    /// <summary>The Git configuration of the tree.</summary>
    private readonly string _gitConfig;

    /// <summary>
    /// Initializes a new instance of the <see cref="ContentOverwriteApplierEscapeTests"/> class.
    /// </summary>
    public ContentOverwriteApplierEscapeTests()
    {
        _tree = Path.Combine(_root, "tree");
        Directory.CreateDirectory(Path.Combine(_tree, ".git"));
        Directory.CreateDirectory(Path.Combine(_tree, "real"));
        _gitConfig = Path.Combine(_tree, ".git", "config");
        File.WriteAllText(_gitConfig, "[core]\n");
    }

    /// <summary>
    /// Verifies that a file below a link the same patch creates is not written through the link, and that the whole
    /// patch is taken back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Link_Created_By_The_Patch_Is_Not_Written_Through()
    {
        var apply = () => Apply(LinkEntry("w", "real"), FileEntry("w/x.txt", "through the link\n"));

        await apply.Should().ThrowAsync<PatchApplyException>();

        File.Exists(Path.Combine(_tree, "real", "x.txt")).Should().BeFalse("nothing is written through a link");
        PhysicalPath.LinkTargetOf(Path.Combine(_tree, "w")).Should().BeNull("the link of the refused patch is taken back");
    }

    /// <summary>
    /// Verifies that links which each point inside the tree by their text, but together lead above it, are refused and
    /// taken back, and that no file is written above the tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Chain_Of_Links_That_Leaves_The_Tree_Is_Refused()
    {
        var apply = () => Apply(
            LinkEntry("d/u", ".."),
            LinkEntry("w", "d/u/.."),
            FileEntry("w/escaped.txt", "outside\n"));

        await apply.Should().ThrowAsync<PatchApplyException>();

        File.Exists(Path.Combine(_root, "escaped.txt")).Should().BeFalse("a patch never writes above the tree");
        PhysicalPath.LinkTargetOf(Path.Combine(_tree, "w")).Should().BeNull();
        PhysicalPath.LinkTargetOf(Path.Combine(_tree, "d", "u")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that two links that lead above the tree only together are refused even with no file below them: the
    /// second one, followed by the file system, ends one level above the tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Link_That_Leads_Above_The_Tree_Through_Another_Link_Is_Taken_Back()
    {
        var apply = () => Apply(LinkEntry("d/u", ".."), LinkEntry("w", "d/u/.."));

        await apply.Should().ThrowAsync<PatchApplyException>();

        PhysicalPath.LinkTargetOf(Path.Combine(_tree, "w")).Should().BeNull();
        PhysicalPath.LinkTargetOf(Path.Combine(_tree, "d", "u")).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a link into Git metadata is refused before anything is written, with the reason worded for the user.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Link_Into_Git_Metadata_Is_Refused()
    {
        var apply = () => Apply(LinkEntry("sub/g", "../.git"));

        (await apply.Should().ThrowAsync<PatchApplyException>())
            .Which.Message.Should().Be(TestTexts.English.Format("Apply.SymlinkGitMetadata", "sub/g", "../.git"));
        Directory.Exists(Path.Combine(_tree, "sub")).Should().BeFalse("nothing was written");
        File.ReadAllText(_gitConfig).Should().Be("[core]\n");
    }

    /// <summary>
    /// Verifies that a write below a link that is already in the tree is refused with the reason worded for the user.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Write_Below_An_Existing_Link_Is_Refused()
    {
        CreateDirectoryLink(Path.Combine(_tree, "linked"), Path.Combine(_tree, "real"));

        var apply = () => Apply(FileEntry("linked/x.txt", "through the link\n"));

        (await apply.Should().ThrowAsync<PatchApplyException>())
            .Which.Message.Should().Be(TestTexts.English.Format("Apply.BelowLink", "linked/x.txt"));
        File.Exists(Path.Combine(_tree, "real", "x.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that links which stay inside the tree, also through another link, are still put in place.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Links_That_Stay_Inside_The_Tree_Are_Applied()
    {
        var apply = () => Apply(LinkEntry("d/u", ".."), LinkEntry("l", "d/u/real"), FileEntry("plain.txt", "plain\n"));

        if (!CanCreateSymbolicLinks())
        {
            // The link itself cannot be made here; the patch must say so instead of being half applied.
            await apply.Should().ThrowAsync<PatchApplyException>();
            File.Exists(Path.Combine(_tree, "plain.txt")).Should().BeFalse();
            return;
        }

        (await apply()).Status.Should().Be(PatchStatus.Applied);
        PhysicalPath.Resolve(Path.Combine(_tree, "l")).Should().Be(PhysicalPath.Resolve(Path.Combine(_tree, "real")));
        File.ReadAllText(Path.Combine(_tree, "plain.txt")).Should().Be("plain\n");
    }

    /// <summary>
    /// Deletes the temporary directories, removing links first so the cleanup never follows one.
    /// </summary>
    public void Dispose()
    {
        try
        {
            foreach (var link in new[] { "w", "g", "l", "linked", Path.Combine("d", "u"), Path.Combine("sub", "g") })
            {
                var path = Path.Combine(_tree, link);
                if (PhysicalPath.LinkTargetOf(path) is not null)
                {
                    if (new DirectoryInfo(path).Exists)
                    {
                        Directory.Delete(path);
                    }
                    else
                    {
                        File.Delete(path);
                    }
                }
            }

            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Builds a change that creates a symbolic link.
    /// </summary>
    /// <param name="path">The path of the link in the tree.</param>
    /// <param name="target">The target of the link.</param>
    /// <returns>The change with the target as its content.</returns>
    private static (FileChange Change, string Content) LinkEntry(string path, string target) => (Change(path, target, isLink: true), target);

    /// <summary>
    /// Builds a change that writes a regular file.
    /// </summary>
    /// <param name="path">The path of the file in the tree.</param>
    /// <param name="content">The content of the file.</param>
    /// <returns>The change with its content.</returns>
    private static (FileChange Change, string Content) FileEntry(string path, string content) => (Change(path, content, isLink: false), content);

    /// <summary>
    /// Builds an added entry whose hash and size match its content.
    /// </summary>
    /// <param name="path">The path in the tree.</param>
    /// <param name="content">The content.</param>
    /// <param name="isLink">Whether the entry is a symbolic link.</param>
    /// <returns>The change.</returns>
    private static FileChange Change(string path, string content, bool isLink)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        return new FileChange(ChangeKind.Added, path, DefaultStateHasher.Sha256Hex(bytes), bytes.Length, isLink);
    }

    /// <summary>
    /// Applies a patch with the given entries, in their order, to the tree.
    /// </summary>
    /// <param name="entries">The changes with their content.</param>
    /// <returns>The result.</returns>
    private Task<PatchApplyResult> Apply(params (FileChange Change, string Content)[] entries)
    {
        var hash = new string('b', 64);
        var envelope = new PatchEnvelope(Guid.NewGuid(), new NodeId("workstation-1"), new NodeId("origin-1"), DateTimeOffset.UtcNow, hash, hash, entries.Select(entry => entry.Change).ToArray());
        var content = new MapContent(entries.ToDictionary(entry => entry.Change.Path, entry => entry.Content));
        return new ContentOverwriteApplier(TestTexts.English, new PathUtility(TestTexts.English)).ApplyAsync(envelope, content, _tree);
    }

    /// <summary>
    /// Checks whether this process may create a symbolic link to a file.
    /// </summary>
    /// <returns><see langword="true"/> when it may.</returns>
    private bool CanCreateSymbolicLinks()
    {
        var probe = Path.Combine(_root, "probe-link");
        try
        {
            File.CreateSymbolicLink(probe, "tree");
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Creates a link to a directory: a symbolic link, or on Windows without the privilege for one, a junction.
    /// </summary>
    /// <param name="link">The link to create.</param>
    /// <param name="target">The directory to point to.</param>
    private static void CreateDirectoryLink(string link, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(link, target);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows() && ex is IOException or UnauthorizedAccessException)
        {
            var info = new System.Diagnostics.ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", link, target })
            {
                info.ArgumentList.Add(argument);
            }

            using var process = System.Diagnostics.Process.Start(info);
            process?.WaitForExit(10_000);
        }

        PhysicalPath.LinkTargetOf(link).Should().NotBeNull("the test needs a link to a directory");
    }

    /// <summary>
    /// Patch content served by path.
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
            => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(_content[change.Path])));
    }
}
