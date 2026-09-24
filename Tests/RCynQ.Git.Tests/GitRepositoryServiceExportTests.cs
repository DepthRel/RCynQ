using System.Text;
using FluentAssertions;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests for <see cref="GitRepositoryService.ExportTreeAsync"/> that run the real <c>git</c> executable.
/// They return early when git is not installed.
/// </summary>
public class GitRepositoryServiceExportTests
{
    /// <summary>
    /// Verifies that exporting a commit produces exactly its tracked files, with none of the Git metadata and none of the
    /// working tree's uncommitted changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportTreeAsync_Extracts_Exactly_The_Tracked_Files_Of_The_Commit()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var repo = new TempDirectory();
        using var destination = new TempDirectory();
        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        await RunGit(git, repo.Path, "init", "-b", "main");
        repo.Write("a.txt", "v1\n");
        repo.Write("sub/b.txt", "v1\n");
        await Commit(git, repo.Path, "v1");
        var commit = (await git.RunAsync(new[] { "rev-parse", "HEAD" }, repo.Path)).StandardOutput.Trim();

        // An uncommitted change and an untracked file must not leak into the export.
        repo.Write("a.txt", "uncommitted\n");
        repo.Write("untracked.txt", "new\n");

        var service = new GitRepositoryService(git);
        await service.ExportTreeAsync(repo.Path, commit, destination.Path);

        // A checkout may write CRLF (core.autocrlf of the machine); the content is what matters here.
        File.ReadAllText(destination["a.txt"]).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("v1\n");
        File.ReadAllText(destination["sub/b.txt"]).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("v1\n");
        File.Exists(destination["untracked.txt"]).Should().BeFalse();
        Directory.Exists(destination[".git"]).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that an unresolvable commit-ish is reported as a Git failure rather than crashing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportTreeAsync_Throws_For_An_Unresolvable_Ref()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var repo = new TempDirectory();
        using var destination = new TempDirectory();
        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        await RunGit(git, repo.Path, "init", "-b", "main");
        repo.Write("a.txt", "v1\n");
        await Commit(git, repo.Path, "v1");

        var service = new GitRepositoryService(git);
        var act = () => service.ExportTreeAsync(repo.Path, "refs/remotes/dest/does-not-exist", destination.Path);

        await act.Should().ThrowAsync<Core.Exceptions.GitExecutionException>();
    }

    /// <summary>
    /// Verifies that a commit-ish starting with a dash is refused before it reaches git, so it cannot be read as an option.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task ExportTreeAsync_Rejects_A_Treeish_That_Looks_Like_An_Option()
    {
        using var repo = new TempDirectory();
        using var destination = new TempDirectory();
        var git = new GitCommandRunner(new SystemProcessRunner(), new GitOptions());
        var service = new GitRepositoryService(git);

        var act = () => service.ExportTreeAsync(repo.Path, "--upload-pack=evil", destination.Path);

        await act.Should().ThrowAsync<ArgumentException>();
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
