using FluentAssertions;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests for the bundle operations of <see cref="GitRepositoryService"/> on real repositories. They return early when git is not installed.
/// </summary>
public class GitBundleRepositoryTests
{
    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _runner = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that a bundle holds only what the receiver lacks, and that there is no bundle when the receiver has everything.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Bundle_Is_Incremental_And_Empty_When_Nothing_Is_New()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var source = new TempDirectory();
        using var target = new TempDirectory();
        using var work = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await InitAsync(source.Path);
        source.Write("a.txt", "one\n");
        var first = await CommitAsync(source.Path, "first");
        source.Write("b.txt", "two\n");
        var second = await CommitAsync(source.Path, "second");

        var full = Path.Combine(work.Path, "full.bundle");
        var increment = Path.Combine(work.Path, "increment.bundle");
        var nothing = Path.Combine(work.Path, "nothing.bundle");

        (await service.CreateBundleAsync(source.Path, full, Array.Empty<string>())).Should().BeTrue();
        (await service.CreateBundleAsync(source.Path, increment, new[] { first })).Should().BeTrue();
        (await service.CreateBundleAsync(source.Path, nothing, new[] { second })).Should().BeFalse("the receiver already has everything");
        File.Exists(nothing).Should().BeFalse();
        new FileInfo(increment).Length.Should().BeLessThan(new FileInfo(full).Length, "the increment leaves out the first commit");

        await service.InitializeAsync(target.Path, "main");
        await service.FetchBundleAsync(target.Path, full, "rcynq");
        (await service.GetKnownCommitsAsync(target.Path, "rcynq")).Should().BeEmpty("nothing is checked out yet, and no local branch exists");
        await service.AdoptRemoteBranchAsync(target.Path, "rcynq", "main", keepWorkingFiles: false);
        File.ReadAllText(Path.Combine(target.Path, "b.txt")).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("two\n");
        (await service.GetKnownCommitsAsync(target.Path, "rcynq")).Should().Equal(second);
    }

    /// <summary>
    /// Verifies that only well-formed names of commits that exist are kept, and that malformed names cannot reach Git as options.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Only_Existing_Well_Formed_Commits_Are_Kept()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var source = new TempDirectory();
        using var work = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await InitAsync(source.Path);
        source.Write("a.txt", "one\n");
        var commit = await CommitAsync(source.Path, "first");

        var kept = await service.FilterExistingCommitsAsync(source.Path, new[] { commit, new string('0', 40), "--upload-pack=evil", "HEAD", commit.ToUpperInvariant(), string.Empty });
        var create = () => service.CreateBundleAsync(source.Path, Path.Combine(work.Path, "x.bundle"), new[] { "--all" });

        kept.Should().Equal(commit);
        await create.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies that adopting a branch can keep the files of the folder, so that their differences show up as local changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Adopting_A_Branch_Can_Keep_The_Files_Of_The_Folder()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var source = new TempDirectory();
        using var target = new TempDirectory();
        using var work = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await InitAsync(source.Path);
        source.Write("a.txt", "one\n");
        await CommitAsync(source.Path, "first");
        var bundle = Path.Combine(work.Path, "all.bundle");
        (await service.CreateBundleAsync(source.Path, bundle, Array.Empty<string>())).Should().BeTrue();
        target.Write("a.txt", "edited here\n");
        await service.InitializeAsync(target.Path, "main");
        await service.FetchBundleAsync(target.Path, bundle, "rcynq");

        await service.AdoptRemoteBranchAsync(target.Path, "rcynq", "main", keepWorkingFiles: true);

        File.ReadAllText(Path.Combine(target.Path, "a.txt")).Should().Be("edited here\n");
        (await _runner.RunAsync(new[] { "status", "--porcelain" }, target.Path)).StandardOutput.Should().Contain("a.txt");
        (await service.GetCurrentBranchAsync(target.Path)).Should().Be("main");
        (await service.HasCommitsAsync(target.Path)).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that once the remote is ensured, plain Git tooling can create a local tracking branch from a fetched branch on its
    /// own (<c>git checkout &lt;branch&gt;</c> and <c>git checkout --track</c>), which fails without a registered remote. This
    /// reproduces the exact failure reported against RCynQ: switching to a second branch of Origin after the first pull adopted
    /// another one, through VS Code's <c>git checkout -q --track rcynq/&lt;branch&gt;</c>.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ensuring_The_Remote_Lets_Git_Checkout_Auto_Track_A_Second_Fetched_Branch()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var source = new TempDirectory();
        using var target = new TempDirectory();
        using var work = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await InitAsync(source.Path);
        source.Write("a.txt", "one\n");
        await CommitAsync(source.Path, "first");
        (await _runner.RunAsync(new[] { "checkout", "-b", "feature/x" }, source.Path)).ExitCode.Should().Be(0);
        source.Write("b.txt", "two\n");
        await CommitAsync(source.Path, "second");
        var bundle = Path.Combine(work.Path, "all.bundle");
        (await service.CreateBundleAsync(source.Path, bundle, Array.Empty<string>())).Should().BeTrue();
        await service.InitializeAsync(target.Path, "main");
        await service.FetchBundleAsync(target.Path, bundle, "rcynq");
        await service.AdoptRemoteBranchAsync(target.Path, "rcynq", "main", keepWorkingFiles: false);

        // Without a registered remote, neither plain DWIM nor an explicit --track can create a local tracking branch from a bare
        // name: this is the exact failure reported against RCynQ ("starting point '...' is not a branch").
        var beforeDwim = await _runner.RunAsync(new[] { "checkout", "feature/x" }, target.Path);
        var beforeTrack = await _runner.RunAsync(new[] { "checkout", "-q", "--track", "rcynq/feature/x" }, target.Path);
        beforeDwim.ExitCode.Should().NotBe(0, "no remote is registered yet, so Git has nothing to match 'feature/x' against");
        beforeTrack.ExitCode.Should().NotBe(0, "git checkout --track refuses when it cannot find a remote that owns the ref (reported upstream as 'is not a branch')");

        await service.EnsureRemoteAsync(target.Path, "rcynq", "file:///placeholder", CancellationToken.None);

        (await _runner.RunAsync(new[] { "checkout", "feature/x" }, target.Path)).ExitCode.Should().Be(0, "plain checkout now finds the branch through the registered remote");
        (await service.GetCurrentBranchAsync(target.Path)).Should().Be("feature/x");
        File.ReadAllText(Path.Combine(target.Path, "b.txt")).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("two\n");
        (await _runner.RunAsync(new[] { "checkout", "main" }, target.Path)).ExitCode.Should().Be(0, "switching back to the already-adopted branch keeps working");
    }

    /// <summary>
    /// Verifies that ensuring the remote is idempotent and never overwrites a URL that is already configured.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Ensuring_The_Remote_Is_Idempotent_And_Never_Overwrites_An_Existing_Url()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var directory = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);
        await InitAsync(directory.Path);
        (await _runner.RunAsync(new[] { "remote", "add", "rcynq", "ssh://real-remote-someone-configured-by-hand/repo" }, directory.Path)).ExitCode.Should().Be(0);

        await service.EnsureRemoteAsync(directory.Path, "rcynq", "file:///placeholder", CancellationToken.None);
        await service.EnsureRemoteAsync(directory.Path, "rcynq", "file:///placeholder", CancellationToken.None);

        (await _runner.RunAsync(new[] { "remote", "get-url", "rcynq" }, directory.Path)).StandardOutput.Trim()
            .Should().Be("ssh://real-remote-someone-configured-by-hand/repo", "an existing remote must never be silently replaced");
    }

    /// <summary>
    /// Verifies that names that are passed to Git are validated.
    /// </summary>
    /// <param name="branch">The branch name.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("--evil")]
    [InlineData("a..b")]
    [InlineData("")]
    [InlineData("with space")]
    public async Task Unsafe_Branch_Names_Are_Refused(string branch)
    {
        using var directory = new TempDirectory();
        var service = new GitRepositoryService(TestTexts.English, _runner);

        var initialize = () => service.InitializeAsync(directory.Path, branch);
        var adopt = () => service.AdoptRemoteBranchAsync(directory.Path, "rcynq", branch, keepWorkingFiles: false);

        await initialize.Should().ThrowAsync<ArgumentException>();
        await adopt.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Creates a repository with an identity for its commits.
    /// </summary>
    /// <param name="directory">The directory.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task InitAsync(string directory)
    {
        (await _runner.RunAsync(new[] { "init", "--quiet", "--initial-branch=main" }, directory)).ExitCode.Should().Be(0);
    }

    /// <summary>
    /// Commits everything in a repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="message">The message.</param>
    /// <returns>The full name of the new commit.</returns>
    private async Task<string> CommitAsync(string directory, string message)
    {
        (await _runner.RunAsync(new[] { "add", "-A" }, directory)).ExitCode.Should().Be(0);
        (await _runner.RunAsync(new[] { "-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", message }, directory)).ExitCode.Should().Be(0);
        return (await _runner.RunAsync(new[] { "rev-parse", "HEAD" }, directory)).StandardOutput.Trim();
    }
}
