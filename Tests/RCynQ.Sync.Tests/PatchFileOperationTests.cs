using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Sync.Services;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="PatchFileOperation"/> on real temporary repositories. They return early when Git is not installed.
/// </summary>
public sealed class PatchFileOperationTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that a patch holds every file the Synchronization tab lists, also a new one, and that a choice narrows it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Holds_Every_Changed_File_Or_The_Chosen_Ones()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.OriginWork, "new.txt", "new\n");

        var everything = Encoding.UTF8.GetString(await operation.BuildAsync(null));
        var chosen = Encoding.UTF8.GetString(await operation.BuildAsync(new[] { "new.txt" }));

        everything.Should().Contain("a/a.txt").And.Contain("b/new.txt");
        chosen.Should().Contain("b/new.txt").And.NotContain("a/a.txt");
    }

    /// <summary>
    /// Verifies that a node without changes gets an empty patch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Build_Is_Empty_Without_Changes()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        (await _fixture.CreatePatchFileOperation(_fixture.OriginNode).BuildAsync(null)).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a working tree without Git saves its changes against the manual snapshot and can take them out again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Working_Tree_Without_Git_Saves_And_Removes_A_Patch()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        await _fixture.WorkstationSyncBase.RebuildAsync();
        var operation = _fixture.CreatePatchFileOperation(_fixture.WorkstationNode);
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.WorkstationWork, "new.txt", "new\n");

        var patch = await operation.BuildAsync(null);
        var removed = await operation.ApplyAsync(patch, null, reverse: true);

        removed.Succeeded.Should().BeTrue(removed.Output);
        ReadWorkstation("a.txt").Should().Be("v1\n");
        File.Exists(Path.Combine(_fixture.WorkstationWork, "new.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a patch can be taken out and brought back on an Origin, which leaves its repository as it was: the
    /// index included, also for what was staged by hand.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Removes_And_Applies_A_Patch_Without_Touching_Its_Index()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        await RunAsync(_fixture.OriginWork, "add", "a.txt");
        var patch = await operation.BuildAsync(null);
        var index = await File.ReadAllBytesAsync(Path.Combine(_fixture.OriginWork, ".git", "index"));

        var removed = await operation.ApplyAsync(patch, null, reverse: true);

        removed.Succeeded.Should().BeTrue(removed.Output);
        Normalized(SyncFixture.Read(_fixture.OriginWork, "a.txt")).Should().Be("v1\n");
        (await File.ReadAllBytesAsync(Path.Combine(_fixture.OriginWork, ".git", "index"))).Should().Equal(index);

        var applied = await operation.ApplyAsync(patch, null, reverse: false);

        applied.Succeeded.Should().BeTrue(applied.Output);
        Normalized(SyncFixture.Read(_fixture.OriginWork, "a.txt")).Should().Be("v2\n");
    }

    /// <summary>
    /// Verifies the rule of an Origin over the whole way of a patch: saved, taken out, listed and brought back, with the look
    /// at the working tree after each step, and every file below <c>.git</c> stays as it was. Taking a patch out leaves files
    /// with the old content and a new time stamp, the case in which <c>git diff</c> would rewrite the index.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Keeps_Every_File_Of_Its_Repository()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        var before = HashRepository(_fixture.OriginWork);
        File.SetLastWriteTimeUtc(Path.Combine(_fixture.OriginWork, "b.txt"), DateTime.UtcNow.AddHours(1));
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.OriginWork, "new.txt", "new\n");

        var patch = await operation.BuildAsync(null);
        (await operation.ApplyAsync(patch, null, reverse: true)).Succeeded.Should().BeTrue();
        await operation.ReadAsync(patch);
        (await operation.ApplyAsync(patch, null, reverse: false)).Succeeded.Should().BeTrue();

        HashRepository(_fixture.OriginWork).Should().Equal(before);
    }

    /// <summary>
    /// Verifies that removing a patch on a Workstation also takes its files out of the index, so the next commit does not
    /// bring back what the user has just removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Workstation_Clears_The_Index_Of_The_Removed_Files()
    {
        if (!await GitIntegrationAsync(_fixture.WorkstationWork))
        {
            return;
        }

        await RunAsync(_fixture.WorkstationWork, "update-ref", "refs/remotes/rcynq/main", "HEAD");
        var operation = _fixture.CreatePatchFileOperation(_fixture.WorkstationNode);
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.WorkstationWork, "new.txt", "new\n");
        await RunAsync(_fixture.WorkstationWork, "add", "-A");

        var removed = await operation.ApplyAsync(await operation.BuildAsync(null), null, reverse: true);

        removed.Succeeded.Should().BeTrue(removed.Output);
        (await OutputAsync(_fixture.WorkstationWork, "status", "--porcelain")).Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that the list of a patch names its files and that only the chosen ones are applied.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Apply_Brings_In_Only_The_Chosen_Files()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        SyncFixture.Write(_fixture.OriginWork, "b.txt", "v2\n");
        var patch = await operation.BuildAsync(null);
        (await operation.ApplyAsync(patch, null, reverse: true)).Succeeded.Should().BeTrue();

        (await operation.ReadAsync(patch)).Select(file => file.Path).Should().BeEquivalentTo("a.txt", "b.txt");

        var applied = await operation.ApplyAsync(patch, new[] { "b.txt" }, reverse: false);

        applied.Succeeded.Should().BeTrue(applied.Output);
        applied.Message.Should().Be(TestTexts.English.Format("PatchFile.Applied", 1));
        Normalized(SyncFixture.Read(_fixture.OriginWork, "a.txt")).Should().Be("v1\n");
        Normalized(SyncFixture.Read(_fixture.OriginWork, "b.txt")).Should().Be("v2\n");
    }

    /// <summary>
    /// Verifies that a patch that does not fit is explained in the words of the program, with the file named.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Does_Not_Fit_Is_Explained()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        var patch = await operation.BuildAsync(null);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v3\n");

        var result = await operation.ApplyAsync(patch, null, reverse: true);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Be(TestTexts.English.Format("PatchFile.Error.DoesNotApply", "  a.txt"));
        result.Output.Should().Contain("a.txt");
    }

    /// <summary>
    /// Verifies that a file without a patch is explained when it is read and when it is applied.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_Without_A_Patch_Is_Explained()
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        var junk = "not a patch\n"u8.ToArray();

        var read = () => operation.ReadAsync(junk);
        var applied = await operation.ApplyAsync(junk, null, reverse: false);

        (await read.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Be(TestTexts.English.Get("PatchFile.Error.NotAPatch"));
        applied.Succeeded.Should().BeFalse();
        applied.Message.Should().Be(TestTexts.English.Get("PatchFile.Error.NotAPatch"));
    }

    /// <summary>
    /// Verifies that a choice of nothing, or of a file the patch does not have, changes nothing.
    /// </summary>
    /// <param name="path">The chosen file; <see langword="null"/> for an empty choice.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("missing.txt")]
    public async Task A_Choice_Outside_The_Patch_Is_Refused(string? path)
    {
        if (!await GitIntegrationAsync(_fixture.OriginWork))
        {
            return;
        }

        var operation = _fixture.CreatePatchFileOperation(_fixture.OriginNode);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v2\n");
        var patch = await operation.BuildAsync(null);
        SyncFixture.Write(_fixture.OriginWork, "a.txt", "v1\n");

        var apply = () => operation.ApplyAsync(patch, path is null ? Array.Empty<string>() : new[] { path }, reverse: false);

        await apply.Should().ThrowAsync<SyncBridgeException>();
        Normalized(SyncFixture.Read(_fixture.OriginWork, "a.txt")).Should().Be("v1\n");
    }

    /// <summary>
    /// Verifies that a Relay, which has no working tree, refuses patch files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Relay_Has_No_Patch_Files()
    {
        var relay = new NodeConfig(new NodeId("relay-1"), "Relay", NodeRole.Relay, null, null);
        var operation = _fixture.CreatePatchFileOperation(relay);

        var build = () => operation.BuildAsync(null);
        var apply = () => operation.ApplyAsync("x"u8.ToArray(), null, reverse: false);

        await build.Should().ThrowAsync<SyncBridgeException>();
        await apply.Should().ThrowAsync<SyncBridgeException>();
    }

    /// <summary>
    /// Deletes the fixture.
    /// </summary>
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Takes the line endings of the machine out of a text.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with <c>\n</c> line endings.</returns>
    private static string? Normalized(string? text) => text?.ReplaceLineEndings("\n");

    /// <summary>
    /// Hashes every file below <c>.git</c> of a repository.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <returns>The relative paths with their hashes.</returns>
    private static string[] HashRepository(string directory)
    {
        var root = Path.Combine(directory, ".git");
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal)
            .Select(file => $"{Path.GetRelativePath(root, file)}={Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))}")
            .ToArray();
    }

    /// <summary>
    /// Tells whether Git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when it is.</returns>
    private async Task<bool> GitAvailableAsync()
    {
        try
        {
            return (await _git.RunAsync(new[] { "--version" }, null)).ExitCode == 0;
        }
        catch (SyncBridgeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Makes a working tree a Git repository with one commit, when Git is installed.
    /// </summary>
    /// <param name="directory">The working tree.</param>
    /// <returns><see langword="true"/> when the repository is ready.</returns>
    private async Task<bool> GitIntegrationAsync(string directory)
    {
        if (!await GitAvailableAsync())
        {
            return false;
        }

        await RunAsync(directory, "init", "-b", "main");
        await RunAsync(directory, "config", "core.autocrlf", "false");
        await RunAsync(directory, "config", "user.name", "t");
        await RunAsync(directory, "config", "user.email", "t@t");
        await RunAsync(directory, "add", "-A");
        await RunAsync(directory, "commit", "-m", "base");
        return true;
    }

    /// <summary>
    /// Runs git and fails the test when git fails.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the command.</returns>
    private async Task RunAsync(string directory, params string[] arguments) => await OutputAsync(directory, arguments);

    /// <summary>
    /// Runs git and fails the test when git fails.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The standard output.</returns>
    private async Task<string> OutputAsync(string directory, params string[] arguments)
    {
        var result = await _git.RunInWorkingTreeAsync(arguments, directory);
        result.ExitCode.Should().Be(0, result.StandardError);
        return result.StandardOutput;
    }

    /// <summary>
    /// Reads a file of the Workstation working tree without the line endings of the machine.
    /// </summary>
    /// <param name="path">The relative path.</param>
    /// <returns>The text.</returns>
    private string? ReadWorkstation(string path) => Normalized(SyncFixture.Read(_fixture.WorkstationWork, path));
}
