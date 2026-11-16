using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests of <see cref="GitBranchService"/> against the real <c>git</c> executable. They return early when git
/// is not installed.
/// </summary>
public sealed class GitBranchServiceTests : IDisposable
{
    /// <summary>The remote-tracking namespace of the Origin in the tests.</summary>
    private const string REMOTE = "rcynq";

    /// <summary>The repository under test.</summary>
    private readonly TempDirectory _repo = new();

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The service under test.</summary>
    private readonly GitBranchService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBranchServiceTests"/> class.
    /// </summary>
    public GitBranchServiceTests()
    {
        _service = new GitBranchService(_git);
    }

    /// <summary>
    /// Verifies that the list has one row per name, with the status against the Origin and the divergence of a pulled branch,
    /// and leaves out the record of the checked out branch of the Origin.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task List_Marks_Pulled_Not_Pulled_And_Local_Branches()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        var second = await CommitAsync("a.txt", "v2\n");
        await RunAsync("update-ref", $"refs/remotes/{REMOTE}/main", first);
        await RunAsync("update-ref", $"refs/remotes/{REMOTE}/feature/x", second);
        await RunAsync("branch", "experiment");
        await RunAsync("symbolic-ref", $"refs/remotes/{REMOTE}/HEAD", $"refs/remotes/{REMOTE}/main");

        var branches = await _service.ListSwitchableAsync(_repo.Path, REMOTE);

        branches.Should().Equal(
            new GitSwitchableBranch("experiment", BranchSyncStatus.LocalOnly, 0, 0),
            new GitSwitchableBranch("feature/x", BranchSyncStatus.NotPulled, 0, 0),
            new GitSwitchableBranch("main", BranchSyncStatus.Pulled, 1, 0));
    }

    /// <summary>
    /// Verifies that a branch of the Origin becomes a local branch on its commit, and that a local branch is switched to as it is.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switch_Creates_A_Local_Branch_From_The_Origin_Or_Takes_The_Local_One()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        var second = await CommitAsync("a.txt", "v2\n");
        await RunAsync("update-ref", $"refs/remotes/{REMOTE}/old", first);

        var created = await _service.SwitchAsync(_repo.Path, REMOTE, "old");

        created.Succeeded.Should().BeTrue(created.Output);
        created.Created.Should().BeTrue();
        (await OutputAsync("rev-parse", "HEAD")).Should().Be(first);
        Normalized(File.ReadAllText(_repo["a.txt"])).Should().Be("v1\n");

        var back = await _service.SwitchAsync(_repo.Path, REMOTE, "main");

        back.Succeeded.Should().BeTrue(back.Output);
        back.Created.Should().BeFalse();
        (await OutputAsync("rev-parse", "HEAD")).Should().Be(second);
    }

    /// <summary>
    /// Verifies that changes the other branch does not touch come along, and that changes it would overwrite stop the switch,
    /// name the file and leave everything as it was.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Switch_Carries_Changes_Or_Refuses_And_Names_The_Files()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CreateRepositoryAsync();
        await RunAsync("branch", "other");
        await CommitAsync("a.txt", "v2\n");
        _repo.Write("b.txt", "edited\n");

        var carried = await _service.SwitchAsync(_repo.Path, REMOTE, "other");

        carried.Succeeded.Should().BeTrue(carried.Output);
        Normalized(File.ReadAllText(_repo["b.txt"])).Should().Be("edited\n");

        _repo.Write("a.txt", "local\n");
        var refused = await _service.SwitchAsync(_repo.Path, REMOTE, "main");

        refused.Succeeded.Should().BeFalse();
        refused.BlockingFiles.Should().Equal("a.txt");
        (await OutputAsync("symbolic-ref", "--short", "HEAD")).Should().Be("other");
        Normalized(File.ReadAllText(_repo["a.txt"])).Should().Be("local\n");
    }

    /// <summary>
    /// Verifies that a name that is neither a branch nor a branch of the Origin, or that Git could read as an option, is refused.
    /// </summary>
    /// <param name="branch">The name.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("missing")]
    [InlineData("-f")]
    [InlineData("a..b")]
    public async Task Switch_Refuses_An_Unknown_Or_Unsafe_Name(string branch)
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CreateRepositoryAsync();

        var act = () => _service.SwitchAsync(_repo.Path, REMOTE, branch);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that only commits of a detached HEAD that no branch, remote-tracking branch or tag holds are listed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Only_In_A_Detached_Head_Are_Listed()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        (await _service.GetCommitsOnlyInHeadAsync(_repo.Path, 10)).Should().BeEmpty("HEAD is on a branch");

        await RunAsync("checkout", "--quiet", "--detach", first);
        (await _service.GetCommitsOnlyInHeadAsync(_repo.Path, 10)).Should().BeEmpty("main holds the commit");

        var lost = await CommitAsync("a.txt", "detached\n");
        (await _service.GetCommitsOnlyInHeadAsync(_repo.Path, 10)).Should().Equal(lost);
    }

    /// <summary>
    /// Verifies the count of the commits of HEAD that no branch of the Origin has.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Not_On_The_Origin_Are_Counted()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        await CommitAsync("a.txt", "v2\n");
        await CommitAsync("a.txt", "v3\n");
        await RunAsync("update-ref", $"refs/remotes/{REMOTE}/other", first);

        (await _service.CountCommitsNotOnRemoteAsync(_repo.Path, REMOTE)).Should().Be(2);
    }

    /// <summary>
    /// Verifies that the checked out branch of the Origin is recorded like <c>origin/HEAD</c>, and that the record goes away
    /// for a detached HEAD or a branch this computer does not have.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Branch_Of_The_Origin_Is_Recorded_And_Removed()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        await RunAsync("update-ref", $"refs/remotes/{REMOTE}/main", first);

        await _service.SetRemoteHeadAsync(_repo.Path, REMOTE, "main");
        (await _service.GetRemoteHeadAsync(_repo.Path, REMOTE)).Should().Be("main");

        await _service.SetRemoteHeadAsync(_repo.Path, REMOTE, "unknown");
        (await _service.GetRemoteHeadAsync(_repo.Path, REMOTE)).Should().BeNull();

        await _service.SetRemoteHeadAsync(_repo.Path, REMOTE, "main");
        await _service.SetRemoteHeadAsync(_repo.Path, REMOTE, null);
        (await _service.GetRemoteHeadAsync(_repo.Path, REMOTE)).Should().BeNull();
        (await OutputAsync("rev-parse", $"refs/remotes/{REMOTE}/main")).Should().Be(first, "only the record goes, not the branch");
    }

    /// <inheritdoc />
    public void Dispose() => _repo.Dispose();

    /// <summary>
    /// Creates the repository on <c>main</c> with <c>a.txt</c> and <c>b.txt</c>.
    /// </summary>
    /// <returns>The commit.</returns>
    private async Task<string> CreateRepositoryAsync()
    {
        await RunAsync("init", "--quiet", "--initial-branch=main");
        await RunAsync("config", "core.autocrlf", "false");
        _repo.Write("a.txt", "v1\n");
        _repo.Write("b.txt", "v1\n");
        await RunAsync("add", "-A");
        await RunAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        return await OutputAsync("rev-parse", "HEAD");
    }

    /// <summary>
    /// Commits a new content of a file.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="content">The content.</param>
    /// <returns>The commit.</returns>
    private async Task<string> CommitAsync(string path, string content)
    {
        _repo.Write(path, content);
        await RunAsync("add", "-A");
        await RunAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "change");
        return await OutputAsync("rev-parse", "HEAD");
    }

    /// <summary>
    /// Runs git in the repository and asserts success.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunAsync(params string[] arguments) => await OutputAsync(arguments);

    /// <summary>
    /// Runs git in the repository, asserts success and returns what it wrote.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The trimmed output.</returns>
    private async Task<string> OutputAsync(params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, _repo.Path);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
        return result.StandardOutput.Trim();
    }

    /// <summary>
    /// Gives a text with Unix line endings.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with <c>\n</c> line endings.</returns>
    private static string Normalized(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);
}
