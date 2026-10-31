using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests that the look at the changes of a working tree leaves the index of the repository as it is: on an Origin
/// the repository is only read. They return early when git is not installed.
/// </summary>
public sealed class GitRepositoryServiceIndexTests : IDisposable
{
    /// <summary>The command runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The repository of the test.</summary>
    private readonly TempDirectory _repo = new();

    /// <summary>
    /// Verifies that a file with a new time stamp and the old content is no change and does not make Git rewrite the index,
    /// which <c>git diff</c> otherwise does whatever <c>GIT_OPTIONAL_LOCKS</c> says.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Touched_File_Leaves_The_Index_Alone()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync();
        File.SetLastWriteTimeUtc(_repo["a.txt"], DateTime.UtcNow.AddHours(1));
        var before = await File.ReadAllBytesAsync(_repo[".git/index"]);
        var service = new GitRepositoryService(TestTexts.English, _git);

        var listed = await service.GetChangedSinceAsync(_repo.Path, commit);
        var kinds = await service.GetChangedFilesSinceAsync(_repo.Path, commit);

        listed.Should().BeEmpty();
        kinds.Should().BeEmpty();
        (await File.ReadAllBytesAsync(_repo[".git/index"])).Should().Equal(before);
    }

    /// <summary>
    /// Verifies that real changes are still found, staged or not, on the private copy of the index.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Real_Changes_Are_Still_Listed()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CommitAsync();
        _repo.Write("a.txt", "two\n");
        _repo.Write("staged.txt", "new\n");
        _repo.Write("untracked.txt", "new\n");
        await RunGitAsync("add", "staged.txt");
        var service = new GitRepositoryService(TestTexts.English, _git);

        var listed = await service.GetChangedSinceAsync(_repo.Path, commit);
        var kinds = await service.GetChangedFilesSinceAsync(_repo.Path, commit);

        listed.Should().BeEquivalentTo("a.txt", "staged.txt", "untracked.txt");
        kinds["a.txt"].Should().Be(ChangeKind.Modified);
        kinds["staged.txt"].Should().Be(ChangeKind.Added);
        kinds["untracked.txt"].Should().Be(ChangeKind.Added);
    }

    /// <summary>
    /// Releases the temporary folder.
    /// </summary>
    public void Dispose() => _repo.Dispose();

    /// <summary>
    /// Creates the repository with one commit.
    /// </summary>
    /// <returns>The full name of the commit.</returns>
    private async Task<string> CommitAsync()
    {
        await RunGitAsync("init", "-b", "main");
        await RunGitAsync("config", "core.autocrlf", "false");
        _repo.Write("a.txt", "one\n");
        await RunGitAsync("add", "-A");
        await RunGitAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "-m", "base");
        return (await RunGitAsync("rev-parse", "HEAD")).Trim();
    }

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
}
