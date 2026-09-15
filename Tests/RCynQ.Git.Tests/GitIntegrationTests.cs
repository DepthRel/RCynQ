using System.Text;
using FluentAssertions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Paths;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;
using Xunit;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests that run the real <c>git</c> executable. They return early when git is not installed.
/// </summary>
public class GitIntegrationTests
{
    /// <summary>
    /// Verifies that a real no-index diff built from staged directories applies cleanly with <c>git apply</c>,
    /// including binary and non-UTF-8 content.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Built_Diff_Applies_With_Git_Apply()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        using var baseDir = new TempDirectory();
        using var work = new TempDirectory();
        using var target = new TempDirectory();
        foreach (var dir in new[] { baseDir, target })
        {
            dir.Write("mod.txt", "line1\nline2\nline3\n");
            dir.Write("sub/del.txt", "gone\n");
        }

        work.Write("mod.txt", "line1\nCHANGED\nline3\n");
        work.Write("sub/new file.txt", "with space\n");
        File.WriteAllBytes(work["latin1.txt"], new byte[] { 0xE9, (byte)'t', 0xE9, (byte)'\n' });
        File.WriteAllBytes(work["bin.dat"], new byte[] { 0, 1, 2, 255 });

        var paths = new PathUtility();
        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        var builder = new DefaultPatchBuilder(
            new DefaultFileManifestBuilder(new SnapshotFilter()), new DefaultStateHasher(), new GitDiffBuilder(git), new FileBundleBuilder(paths), paths, TimeProvider.System);

        var envelope = await builder.BuildAsync(new NodeId("s"), new NodeId("d"), baseDir.Path, work.Path);
        var diffFile = Path.Combine(target.Path, "..", Guid.NewGuid().ToString("N") + ".diff");
        await File.WriteAllBytesAsync(diffFile, envelope.Diff);
        try
        {
            var apply = await git.RunAsync(new[] { "apply", "--whitespace=nowarn", diffFile }, target.Path);
            apply.ExitCode.Should().Be(0, apply.StandardError);
        }
        finally
        {
            File.Delete(diffFile);
        }

        File.ReadAllText(target["mod.txt"]).Should().Be("line1\nCHANGED\nline3\n");
        File.Exists(target["sub/del.txt"]).Should().BeFalse();
        File.ReadAllText(target["sub/new file.txt"]).Should().Be("with space\n");
        File.ReadAllBytes(target["latin1.txt"]).Should().Equal(0xE9, (byte)'t', 0xE9, (byte)'\n');
        File.ReadAllBytes(target["bin.dat"]).Should().Equal(0, 1, 2, 255);
    }

    /// <summary>
    /// Verifies that the process runner captures output and exit codes and enforces timeouts.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task SystemProcessRunner_Captures_Output_And_Times_Out()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        var runner = new SystemProcessRunner();
        var version = await runner.RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)));
        version.ExitCode.Should().Be(0);
        version.StandardOutput.Should().StartWith("git version");

        var failing = await runner.RunAsync(new ProcessSpec("git", new[] { "definitely-not-a-command" }, null, null, TimeSpan.FromSeconds(30)));
        failing.ExitCode.Should().NotBe(0);
        failing.StandardError.Should().NotBeEmpty();

        var missing = () => runner.RunAsync(new ProcessSpec("rcynq-no-such-executable", Array.Empty<string>(), null, null, null));
        await missing.Should().ThrowAsync<SyncBridgeException>();

        var sleep = OperatingSystem.IsWindows()
            ? new ProcessSpec("powershell", new[] { "-NoProfile", "-Command", "Start-Sleep -Seconds 20" }, null, null, TimeSpan.FromMilliseconds(500))
            : new ProcessSpec("sleep", new[] { "20" }, null, null, TimeSpan.FromMilliseconds(500));
        var slow = () => runner.RunAsync(sleep);
        (await slow.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("timed out");
    }

    /// <summary>
    /// Verifies branch info and fast-forward on real repositories using a local file remote.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Fetch_And_BranchInfo_On_Real_Repositories()
    {
        if (!await GitAvailableAsync())
        {
            return;
        }

        using var dest = new TempDirectory();
        using var source = new TempDirectory();
        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        await RunGit(git, dest.Path, "init", "-b", "main");
        dest.Write("readme.md", "v1");
        await Commit(git, dest.Path, "v1");
        await RunGit(git, source.Path, "clone", dest.Path, ".");

        dest.Write("readme.md", "v2");
        await Commit(git, dest.Path, "v2");

        var service = new GitRepositoryService(git);
        await service.FetchAsync(source.Path, Path.GetFullPath(dest.Path), "dest", null);
        var info = await service.GetBranchInfoAsync(source.Path, "dest");
        info.Should().Be(new Core.State.GitBranchInfo(true, "main", "refs/remotes/dest/main", 0, 1));

        await service.PullFastForwardAsync(source.Path, "dest");
        File.ReadAllText(source["readme.md"]).Should().Be("v2");
        (await service.GetBranchInfoAsync(source.Path, "dest")).Behind.Should().Be(0);
    }

    /// <summary>
    /// Checks whether git can be executed.
    /// </summary>
    /// <returns><see langword="true"/> when git is available.</returns>
    internal static async Task<bool> GitAvailableAsync()
    {
        try
        {
            var result = await new SystemProcessRunner().RunAsync(new ProcessSpec("git", new[] { "--version" }, null, null, TimeSpan.FromSeconds(30)));
            return result.ExitCode == 0;
        }
        catch (SyncBridgeException)
        {
            return false;
        }
    }

    /// <summary>
    /// Commits all files with a fixed identity.
    /// </summary>
    /// <param name="git">The git runner.</param>
    /// <param name="directory">The repository.</param>
    /// <param name="message">The commit message.</param>
    /// <returns>A task representing the operation.</returns>
    private static async Task Commit(IGitCommandRunner git, string directory, string message)
    {
        await RunGit(git, directory, "add", "-A");
        await RunGit(git, directory, "-c", "user.name=RCynQ Test", "-c", "user.email=test@example.invalid", "commit", "-m", message);
    }

    /// <summary>
    /// Runs git and asserts success.
    /// </summary>
    /// <param name="git">The git runner.</param>
    /// <param name="directory">The working directory.</param>
    /// <param name="args">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private static async Task RunGit(IGitCommandRunner git, string directory, params string[] args)
    {
        var result = await git.RunAsync(args, directory);
        result.ExitCode.Should().Be(0, Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(result.StandardError)));
    }
}
