using FluentAssertions;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for <see cref="GitRepositoryService.RenameRemoteAsync"/> on real repositories. They return early when git is not installed.
/// </summary>
public class GitRemoteRenameTests
{
    /// <summary>The placeholder URL of a remote.</summary>
    private const string PLACEHOLDER = "file:///placeholder";

    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _runner = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that a configured remote is renamed together with its branches.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Configured_Remote_Is_Renamed_With_Its_Branches()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var directory = new TempDirectory();
        var commit = await InitWithBranchUnderAsync(directory, "dest");
        await GitAsync(directory.Path, "remote", "add", "dest", PLACEHOLDER);
        var service = new GitRepositoryService(TestTexts.English, _runner);

        (await service.RenameRemoteAsync(directory.Path, "dest", "rcynq", PLACEHOLDER)).Should().BeTrue();

        (await GitAsync(directory.Path, "rev-parse", "refs/remotes/rcynq/main")).Should().Be(commit);
        (await _runner.RunAsync(new[] { "rev-parse", "--verify", "--quiet", "refs/remotes/dest/main" }, directory.Path)).ExitCode.Should().NotBe(0);
        (await GitAsync(directory.Path, "remote")).Should().Be("rcynq");
    }

    /// <summary>
    /// Verifies that branches fetched under the old name without a configured remote are renamed too.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Branches_Without_A_Remote_Are_Renamed()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var directory = new TempDirectory();
        var commit = await InitWithBranchUnderAsync(directory, "dest");
        var service = new GitRepositoryService(TestTexts.English, _runner);

        (await service.RenameRemoteAsync(directory.Path, "dest", "rcynq", PLACEHOLDER)).Should().BeTrue();

        (await GitAsync(directory.Path, "rev-parse", "refs/remotes/rcynq/main")).Should().Be(commit);
        (await GitAsync(directory.Path, "remote", "get-url", "rcynq")).Should().Be(PLACEHOLDER);
    }

    /// <summary>
    /// Verifies that nothing happens when the old name is not there, and that the call can be repeated.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Nothing_To_Rename_Changes_Nothing()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var directory = new TempDirectory();
        await InitWithBranchUnderAsync(directory, "dest");
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await service.RenameRemoteAsync(directory.Path, "dest", "rcynq", PLACEHOLDER);

        (await service.RenameRemoteAsync(directory.Path, "dest", "rcynq", PLACEHOLDER)).Should().BeFalse();

        (await GitAsync(directory.Path, "remote")).Should().Be("rcynq");
    }

    /// <summary>
    /// Verifies that a remote with the new name that already exists is never replaced.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Existing_Target_Remote_Is_Left_Alone()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var directory = new TempDirectory();
        await InitWithBranchUnderAsync(directory, "dest");
        await GitAsync(directory.Path, "remote", "add", "dest", PLACEHOLDER);
        await GitAsync(directory.Path, "remote", "add", "rcynq", "ssh://set-by-hand/repo");
        var service = new GitRepositoryService(TestTexts.English, _runner);

        (await service.RenameRemoteAsync(directory.Path, "dest", "rcynq", PLACEHOLDER)).Should().BeFalse();

        (await GitAsync(directory.Path, "remote", "get-url", "rcynq")).Should().Be("ssh://set-by-hand/repo");
        (await GitAsync(directory.Path, "remote", "get-url", "dest")).Should().Be(PLACEHOLDER);
    }

    /// <summary>
    /// Verifies that remote names passed to Git are validated.
    /// </summary>
    /// <param name="oldName">The old name.</param>
    /// <param name="newName">The new name.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("--evil", "rcynq")]
    [InlineData("dest", "a b")]
    [InlineData("dest", "")]
    public async Task Unsafe_Remote_Names_Are_Refused(string oldName, string newName)
    {
        using var directory = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);

        var rename = () => service.RenameRemoteAsync(directory.Path, oldName, newName, PLACEHOLDER);

        await rename.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Creates a repository with one commit and a remote-tracking branch <c>main</c> under a namespace.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <param name="remoteName">The remote-tracking namespace.</param>
    /// <returns>The full name of the commit.</returns>
    private async Task<string> InitWithBranchUnderAsync(TempDirectory directory, string remoteName)
    {
        await GitAsync(directory.Path, "init", "--quiet", "--initial-branch=main");
        directory.Write("a.txt", "one\n");
        await GitAsync(directory.Path, "add", "-A");
        await GitAsync(directory.Path, "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        var commit = await GitAsync(directory.Path, "rev-parse", "HEAD");
        await GitAsync(directory.Path, "update-ref", $"refs/remotes/{remoteName}/main", commit);
        return commit;
    }

    /// <summary>
    /// Runs git and expects it to succeed.
    /// </summary>
    /// <param name="directory">The working directory.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The trimmed standard output.</returns>
    private async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var result = await _runner.RunAsync(arguments, directory);
        result.ExitCode.Should().Be(0, result.StandardError);
        return result.StandardOutput.Trim();
    }
}
