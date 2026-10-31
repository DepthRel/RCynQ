using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Git.Commands;
using RCynQ.Git.Patches.Files;
using RCynQ.Git.Processes;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests of <see cref="GitPatchFileService"/> that run the real <c>git</c> executable. They return early when
/// git is not installed.
/// </summary>
public sealed class GitPatchFileServiceTests : IDisposable
{
    /// <summary>The command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The repository of the test.</summary>
    private readonly TempDirectory _repo = new();

    /// <summary>The folder of the patch files of the test.</summary>
    private readonly TempDirectory _files = new();

    /// <summary>The service under test.</summary>
    private readonly GitPatchFileService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitPatchFileServiceTests"/> class.
    /// </summary>
    public GitPatchFileServiceTests()
    {
        _service = new GitPatchFileService(_git);
    }

    /// <summary>
    /// Verifies that a patch holds modified, deleted and new files, and nothing else that differs from the commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Holds_The_Named_Files_Of_Every_Kind()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("edited.txt", "one\n"), ("removed.txt", "two\n"), ("other.txt", "three\n"));
        _repo.Write("edited.txt", "ONE\n");
        File.Delete(_repo["removed.txt"]);
        _repo.Write("folder/new.txt", "four\n");
        _repo.Write("other.txt", "not named\n");

        var patch = await _service.BuildAsync(_repo.Path, commit, new[] { "edited.txt", "removed.txt", "folder/new.txt" });

        var text = Encoding.UTF8.GetString(patch);
        text.Should().Contain("a/edited.txt").And.Contain("a/removed.txt").And.Contain("b/folder/new.txt");
        text.Should().NotContain("other.txt");
    }

    /// <summary>
    /// Verifies that building a patch writes nothing into the repository: on an Origin it is only read.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Writes_Nothing_Into_The_Repository()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"));
        _repo.Write("a.txt", "two\n");
        _repo.Write("new.txt", "three\n");
        var before = HashGitDirectory();

        await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt", "new.txt" });

        HashGitDirectory().Should().Equal(before);
    }

    /// <summary>
    /// Verifies that a file name with the characters of a pattern is taken literally, so its neighbours stay out.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Takes_A_File_Name_Literally()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("star[1].txt", "one\n"), ("star1.txt", "two\n"));
        _repo.Write("star[1].txt", "ONE\n");
        _repo.Write("star1.txt", "TWO\n");

        var patch = await _service.BuildAsync(_repo.Path, commit, new[] { "star[1].txt" });

        Encoding.UTF8.GetString(patch).Should().Contain("star[1].txt").And.NotContain("a/star1.txt");
    }

    /// <summary>
    /// Verifies that a patch survives the round trip: text in an old encoding and binary files come back byte by byte.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reverse_And_Apply_Bring_Back_Text_And_Binary_Files_Byte_By_Byte()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("text.txt", "one\ntwo\n"));
        await File.WriteAllBytesAsync(_repo["old.txt"], new byte[] { 0x63, 0xE9, 0x0A });
        var binary = RandomNumberGenerator.GetBytes(4096);
        await File.WriteAllBytesAsync(_repo["data.bin"], binary);
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "more");
        var commit = await HeadAsync();

        var changedBinary = RandomNumberGenerator.GetBytes(4096);
        await File.WriteAllBytesAsync(_repo["data.bin"], changedBinary);
        await File.WriteAllBytesAsync(_repo["old.txt"], new byte[] { 0x64, 0xE9, 0x0A });
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "data.bin", "old.txt" }));

        var removed = await _service.ApplyAsync(_repo.Path, All(patch, reverse: true, "data.bin", "old.txt"));

        removed.Succeeded.Should().BeTrue(removed.Output);
        (await File.ReadAllBytesAsync(_repo["data.bin"])).Should().Equal(binary);
        (await File.ReadAllBytesAsync(_repo["old.txt"])).Should().Equal(0x63, 0xE9, 0x0A);

        var applied = await _service.ApplyAsync(_repo.Path, All(patch, reverse: false, "data.bin", "old.txt"));

        applied.Succeeded.Should().BeTrue(applied.Output);
        (await File.ReadAllBytesAsync(_repo["data.bin"])).Should().Equal(changedBinary);
    }

    /// <summary>
    /// Verifies that removing a patch keeps what was edited after it was built, elsewhere in the same file.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reverse_Keeps_A_Later_Edit_In_Another_Place()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var lines = string.Concat(Enumerable.Range(1, 30).Select(line => $"line {line}\n"));
        var commit = await CommitAsync(("a.txt", lines));
        _repo.Write("a.txt", lines.Replace("line 2\n", "LINE 2\n", StringComparison.Ordinal));
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt" }));
        _repo.Write("a.txt", (await File.ReadAllTextAsync(_repo["a.txt"])).Replace("line 28\n", "LINE 28\n", StringComparison.Ordinal));

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: true, "a.txt"));

        result.Succeeded.Should().BeTrue(result.Output);
        (await File.ReadAllTextAsync(_repo["a.txt"])).Should().Be(lines.Replace("line 28\n", "LINE 28\n", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies that a patch that no longer fits changes nothing at all and names the file that stands in the way.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Does_Not_Fit_Changes_Nothing_And_Names_The_File()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"), ("b.txt", "two\n"));
        _repo.Write("a.txt", "ONE\n");
        _repo.Write("b.txt", "TWO\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt", "b.txt" }));
        _repo.Write("a.txt", "edited again\n");

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: true, "a.txt", "b.txt"));

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().Be(PatchFileFailure.DoesNotApply);
        result.Files.Should().Equal("a.txt");
        (await File.ReadAllTextAsync(_repo["a.txt"])).Should().Be("edited again\n");
        (await File.ReadAllTextAsync(_repo["b.txt"])).Should().Be("TWO\n", "either every file of a patch changes or none does");
    }

    /// <summary>
    /// Verifies that a patch that creates a file which exists already says so.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Creates_An_Existing_File_Is_Reported()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"));
        _repo.Write("new.txt", "two\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "new.txt" }));

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: false, "new.txt"));

        result.Failure.Should().Be(PatchFileFailure.AlreadyExists);
        result.Files.Should().Equal("new.txt");
    }

    /// <summary>
    /// Verifies that a patch that changes a file the working tree does not have says so.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Changes_A_Missing_File_Is_Reported()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"));
        _repo.Write("a.txt", "two\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt" }));
        File.Delete(_repo["a.txt"]);

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: false, "a.txt"));

        result.Failure.Should().Be(PatchFileFailure.MissingFile);
        result.Files.Should().Equal("a.txt");
    }

    /// <summary>
    /// Verifies that a patch that reaches out of the working tree or into the repository itself is refused.
    /// </summary>
    /// <param name="path">The path the patch names.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("../evil.txt")]
    [InlineData(".git/hooks/pre-commit")]
    public async Task A_Patch_With_An_Unsafe_Path_Is_Refused(string path)
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("a.txt", "one\n"));
        var patch = await WritePatchAsync(Encoding.UTF8.GetBytes(
            $"diff --git a/{path} b/{path}\nnew file mode 100644\n--- /dev/null\n+++ b/{path}\n@@ -0,0 +1 @@\n+x\n"));

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: false, path));

        result.Failure.Should().Be(PatchFileFailure.UnsafePath);
        result.Files.Should().Equal(path);
        File.Exists(Path.Combine(_repo.Path, path)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a file that holds no patch lists nothing and is reported as such when applied.
    /// </summary>
    /// <param name="content">The content of the file.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("this is not a patch\n")]
    public async Task A_File_Without_A_Patch_Is_Reported(string content)
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("a.txt", "one\n"));
        var patch = await WritePatchAsync(Encoding.UTF8.GetBytes(content));

        (await _service.ListAsync(_repo.Path, patch)).Should().BeEmpty();

        var result = await _service.ApplyAsync(_repo.Path, All(patch, reverse: false));

        result.Failure.Should().Be(PatchFileFailure.NotAPatch);
    }

    /// <summary>
    /// Verifies that the list tells how a patch changes every file, a renamed file under its new name.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task List_Tells_How_Every_File_Changes()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("a.txt", "one\n"));
        var patch = await WritePatchAsync(Encoding.UTF8.GetBytes(
            "diff --git a/added file.txt b/added file.txt\nnew file mode 100644\n--- /dev/null\n+++ b/added file.txt\n@@ -0,0 +1 @@\n+x\n" +
            "diff --git a/gone.txt b/gone.txt\ndeleted file mode 100644\n--- a/gone.txt\n+++ /dev/null\n@@ -1 +0,0 @@\n-x\n" +
            "diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-one\n+two\n" +
            "diff --git a/old.txt b/moved.txt\nsimilarity index 100%\nrename from old.txt\nrename to moved.txt\n"));

        var files = await _service.ListAsync(_repo.Path, patch);

        files.Select(file => (file.Kind, file.Path)).Should().BeEquivalentTo(new[]
        {
            (ChangeKind.Added, "added file.txt"),
            (ChangeKind.Deleted, "gone.txt"),
            (ChangeKind.Modified, "a.txt"),
            (ChangeKind.Modified, "moved.txt")
        });
    }

    /// <summary>
    /// Verifies that only the chosen files of a patch are applied, also when their names hold the characters of a pattern
    /// or more files are skipped than chosen and the other way round.
    /// </summary>
    /// <param name="chosen">The chosen files.</param>
    /// <param name="skipped">The skipped files.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(new[] { "a[1].txt" }, new[] { "a1.txt", "b.txt" })]
    [InlineData(new[] { "a1.txt", "b.txt" }, new[] { "a[1].txt" })]
    public async Task Apply_Changes_Only_The_Chosen_Files(string[] chosen, string[] skipped)
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a[1].txt", "one\n"), ("a1.txt", "one\n"), ("b.txt", "one\n"));
        foreach (var path in chosen.Concat(skipped))
        {
            _repo.Write(path, "two\n");
        }

        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, chosen.Concat(skipped).ToArray()));
        await RunGitAsync("checkout", "--", ".");

        var result = await _service.ApplyAsync(_repo.Path, new GitPatchApplyRequest(patch, false, chosen, skipped));

        result.Succeeded.Should().BeTrue(result.Output);
        foreach (var path in chosen)
        {
            (await ReadNormalizedAsync(path)).Should().Be("two\n");
        }

        foreach (var path in skipped)
        {
            (await ReadNormalizedAsync(path)).Should().Be("one\n");
        }
    }

    /// <summary>
    /// Verifies that a choice too long for a command line is refused instead of applied in part.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Choice_Too_Long_For_The_Command_Line_Is_Refused()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"));
        _repo.Write("a.txt", "two\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt" }));
        var many = Enumerable.Range(0, 2000).Select(number => $"folder/with/a/rather/long/name/of/a/file-{number}.txt").ToArray();

        var result = await _service.ApplyAsync(_repo.Path, new GitPatchApplyRequest(patch, false, many, many));

        result.Failure.Should().Be(PatchFileFailure.TooManyFiles);
    }

    /// <summary>
    /// Verifies that a working tree in a subfolder of the repository gets patches relative to it and applies them whole:
    /// Git run in a subfolder otherwise skips the files without a word.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Working_Tree_In_A_Subfolder_Applies_The_Whole_Patch()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("top.txt", "top\n"), ("sub/a.txt", "one\n"), ("sub/b.txt", "one\n"));
        var work = _repo["sub"];
        _repo.Write("sub/a.txt", "two\n");
        _repo.Write("sub/inner/new.txt", "two\n");
        _repo.Write("sub/b.txt", "two\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(work, commit, new[] { "a.txt", "b.txt", "inner/new.txt" }));

        var files = await _service.ListAsync(work, patch);
        files.Select(file => file.Path).Should().BeEquivalentTo("a.txt", "b.txt", "inner/new.txt");

        var removed = await _service.ApplyAsync(work, new GitPatchApplyRequest(patch, true, new[] { "a.txt", "inner/new.txt" }, new[] { "b.txt" }));

        removed.Succeeded.Should().BeTrue(removed.Output);
        (await ReadNormalizedAsync("sub/a.txt")).Should().Be("one\n");
        (await ReadNormalizedAsync("sub/b.txt")).Should().Be("two\n");
        File.Exists(_repo["sub/inner/new.txt"]).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a patch applies in a folder without Git too: such a working tree exchanges patches as well.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Folder_Without_Git_Applies_A_Patch()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var plain = new TempDirectory();
        plain.Write("a.txt", "one\n");
        var patch = await WritePatchAsync("diff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-one\n+two\n"u8.ToArray());

        (await _service.ListAsync(plain.Path, patch)).Should().ContainSingle(file => file.Path == "a.txt");

        var result = await _service.ApplyAsync(plain.Path, All(patch, reverse: false, "a.txt"));

        result.Succeeded.Should().BeTrue(result.Output);
        (await File.ReadAllTextAsync(plain["a.txt"])).ReplaceLineEndings("\n").Should().Be("two\n");
    }

    /// <summary>
    /// Verifies that a working tree with CRLF line endings over LF commits gets a patch of the changed line only, which
    /// comes out and goes back in with the line endings of the working tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Line_Endings_Of_The_Working_Tree_Do_Not_Reach_The_Patch()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("placeholder.txt", "x\n"));
        await RunGitAsync("config", "core.autocrlf", "true");
        await File.WriteAllTextAsync(_repo["a.txt"], "one\r\ntwo\r\nthree\r\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("commit", "-m", "lines");
        var commit = await HeadAsync();
        await File.WriteAllTextAsync(_repo["a.txt"], "one\r\nTWO\r\nthree\r\n");

        var bytes = await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt" });
        var patch = await WritePatchAsync(bytes);

        var text = Encoding.UTF8.GetString(bytes);
        text.Should().Contain("-two\n+TWO\n").And.NotContain("\r");

        var removed = await _service.ApplyAsync(_repo.Path, All(patch, reverse: true, "a.txt"));

        removed.Succeeded.Should().BeTrue(removed.Output);
        (await File.ReadAllTextAsync(_repo["a.txt"])).Should().Be("one\r\ntwo\r\nthree\r\n");
    }

    /// <summary>
    /// Verifies that applying a patch leaves the index alone: the changes wait in the working tree, uncommitted.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Apply_Leaves_The_Index_Alone()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync(("a.txt", "one\n"));
        _repo.Write("a.txt", "two\n");
        _repo.Write("new.txt", "three\n");
        var patch = await WritePatchAsync(await _service.BuildAsync(_repo.Path, commit, new[] { "a.txt", "new.txt" }));
        await _service.ApplyAsync(_repo.Path, All(patch, reverse: true, "a.txt", "new.txt"));
        var before = HashGitDirectory();

        await _service.ApplyAsync(_repo.Path, All(patch, reverse: false, "a.txt", "new.txt"));

        HashGitDirectory().Should().Equal(before);
        (await RunGitAsync("diff", "--cached", "--name-only")).Should().BeEmpty();
        (await RunGitAsync("rev-parse", "HEAD")).Trim().Should().Be(commit);
    }

    /// <summary>
    /// Verifies that the changes of the named files leave the index, and that staged changes of other files stay there.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Unstage_Clears_Only_The_Named_Files()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CommitAsync(("a.txt", "one\n"), ("b.txt", "one\n"));
        _repo.Write("a.txt", "two\n");
        _repo.Write("b.txt", "two\n");
        _repo.Write("new[1].txt", "two\n");
        await RunGitAsync("add", "-A");

        (await _service.UnstageAsync(_repo.Path, new[] { "a.txt", "new[1].txt" })).Should().BeTrue();

        (await RunGitAsync("diff", "--cached", "--name-only")).Trim().Should().Be("b.txt");
    }

    /// <summary>
    /// Releases the temporary folders.
    /// </summary>
    public void Dispose()
    {
        _repo.Dispose();
        _files.Dispose();
    }

    /// <summary>
    /// Builds the request that applies every named file.
    /// </summary>
    /// <param name="patch">The patch file.</param>
    /// <param name="reverse">Whether the patch is removed.</param>
    /// <param name="paths">The files.</param>
    /// <returns>The request.</returns>
    private static GitPatchApplyRequest All(string patch, bool reverse, params string[] paths)
        => new(patch, reverse, paths, Array.Empty<string>());

    /// <summary>
    /// Creates the repository with a first commit.
    /// </summary>
    /// <param name="files">The files of the commit.</param>
    /// <returns>The full name of the commit.</returns>
    private async Task<string> CommitAsync(params (string Path, string Content)[] files)
    {
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("config", "core.autocrlf", "false");
        foreach (var (path, content) in files)
        {
            _repo.Write(path, content);
        }

        await RunGitAsync("add", "-A");
        await RunGitAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-m", "base");
        await RunGitAsync("config", "user.name", "t");
        await RunGitAsync("config", "user.email", "t@t");
        return await HeadAsync();
    }

    /// <summary>
    /// Reads the full name of the current commit.
    /// </summary>
    /// <returns>The name.</returns>
    private async Task<string> HeadAsync() => (await RunGitAsync("rev-parse", "HEAD")).Trim();

    /// <summary>
    /// Runs git in the repository and fails the test when git fails.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    private async Task<string> RunGitAsync(params string[] arguments)
    {
        var result = await _git.RunInWorkingTreeAsync(arguments, _repo.Path);
        result.ExitCode.Should().Be(0, result.StandardError);
        return result.StandardOutput;
    }

    /// <summary>
    /// Stores the bytes of a patch in a file.
    /// </summary>
    /// <param name="patch">The bytes.</param>
    /// <returns>The path of the file.</returns>
    private async Task<string> WritePatchAsync(byte[] patch)
    {
        var path = _files[$"{Guid.NewGuid():N}.patch"];
        await File.WriteAllBytesAsync(path, patch);
        return path;
    }

    /// <summary>
    /// Reads a file of the repository without its line endings, which depend on the settings of the machine.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The text with <c>\n</c> line endings.</returns>
    private async Task<string> ReadNormalizedAsync(string path)
        => (await File.ReadAllTextAsync(_repo[path])).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// Hashes every file below <c>.git</c>.
    /// </summary>
    /// <returns>The relative paths with their hashes.</returns>
    private string[] HashGitDirectory()
    {
        var root = _repo[".git"];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)}={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}")
            .ToArray();
    }
}
