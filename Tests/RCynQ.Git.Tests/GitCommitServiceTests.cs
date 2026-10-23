using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Git.Commands;
using RCynQ.Git.Commits;
using RCynQ.Git.Processes;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Tests of <see cref="GitCommitService"/> on real repositories: listing, packing, the quarantine, moving and rolling back a
/// branch. They return early when git is not installed.
/// </summary>
public class GitCommitServiceTests
{
    /// <summary>The Git command runner.</summary>
    private readonly GitCommandRunner _runner = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The service under test.</summary>
    private readonly GitCommitService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommitServiceTests"/> class.
    /// </summary>
    public GitCommitServiceTests()
    {
        _service = new GitCommitService(_runner);
    }

    /// <summary>
    /// Verifies that the unsent commits are listed oldest first along the first-parent chain, and that the chain starts on the base.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Commits_Are_Listed_Oldest_First_Along_The_First_Parent_Chain()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var origin = new TempDirectory();
        using var workstation = new TempDirectory();
        var first = await CreateOriginAsync(origin);
        await CloneAsync(origin.Path, workstation.Path);
        workstation.Write("a.txt", "two\n");
        var second = await CommitAsync(workstation.Path, "second\tcommit");
        workstation.Write("a.txt", "three\n");
        var third = await CommitAsync(workstation.Path, "third");

        var commits = await _service.ListFirstParentAsync(workstation.Path, first, third, 10);
        var chain = await _service.GetFirstParentChainAsync(workstation.Path, first, third, null);

        commits.Select(commit => commit.Hash).Should().Equal(second, third);
        commits[0].Subject.Should().Be("second commit", "a control character of a subject becomes a space");
        commits[0].Author.Should().Be("t <t@t>");
        chain.Select(link => link.Hash).Should().Equal(second, third);
        chain[0].FirstParent.Should().Be(first);
        (await _service.GetFirstParentChainAsync(workstation.Path, third, first, null)).Should().BeEmpty("the base is not an ancestor of the tip");
    }

    /// <summary>
    /// Verifies that a bundle of a part of the commits is unpacked into the quarantine only, and that the repository gets
    /// the objects and moves its branch only when the packs are moved and the branch is fast-forwarded.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Bundle_Of_A_Range_Stays_In_The_Quarantine_Until_Its_Packs_Are_Moved()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var origin = new TempDirectory();
        using var workstation = new TempDirectory();
        using var work = new TempDirectory();
        var first = await CreateOriginAsync(origin);
        await CloneAsync(origin.Path, workstation.Path);
        workstation.Write("b.txt", "new\n");
        var second = await CommitAsync(workstation.Path, "second");
        workstation.Write("c.txt", "later\n");
        await CommitAsync(workstation.Path, "third");
        var bundle = Path.Combine(work.Path, "commits.bundle");

        await _service.CreateBundleAsync(workstation.Path, bundle, first, second);
        var quarantine = await _service.UnbundleAsync(origin.Path, bundle, Path.Combine(work.Path, "objects"));

        quarantine.Heads.Should().ContainSingle().Which.Commit.Should().Be(second);
        (await RefsAsync(workstation.Path)).Should().NotContain("refs/rcynq/", "the reference that named the tip is removed");
        (await HasObjectAsync(origin.Path, second)).Should().BeFalse("nothing reaches the repository before the packs are moved");
        (await _service.GetFirstParentChainAsync(origin.Path, first, second, quarantine)).Select(link => link.Hash).Should().Equal(second);
        (await _service.GetTouchedPathsAsync(origin.Path, first, second, quarantine)).Should().BeEquivalentTo("b.txt");

        var moved = _service.MovePacks(quarantine);
        await _service.FastForwardAsync(origin.Path, second);

        moved.Should().NotBeEmpty();
        (await HeadAsync(origin.Path)).Should().Be(second, "the hashes stay what the Workstation has");
        File.ReadAllText(origin["b.txt"]).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("new\n");
    }

    /// <summary>
    /// Verifies that packs taken back out of the repository leave it without the objects, as before the delivery.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Removed_Packs_Leave_The_Repository_As_It_Was()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var origin = new TempDirectory();
        using var workstation = new TempDirectory();
        using var work = new TempDirectory();
        var first = await CreateOriginAsync(origin);
        await CloneAsync(origin.Path, workstation.Path);
        workstation.Write("b.txt", "new\n");
        var second = await CommitAsync(workstation.Path, "second");
        var bundle = Path.Combine(work.Path, "commits.bundle");
        await _service.CreateBundleAsync(workstation.Path, bundle, first, second);
        var quarantine = await _service.UnbundleAsync(origin.Path, bundle, Path.Combine(work.Path, "objects"));
        var before = PackFiles(origin.Path);

        var moved = _service.MovePacks(quarantine);
        (await HasObjectAsync(origin.Path, second)).Should().BeTrue();
        _service.RemovePacks(moved);

        (await HasObjectAsync(origin.Path, second)).Should().BeFalse();
        PackFiles(origin.Path).Should().BeEquivalentTo(before);
        (await HeadAsync(origin.Path)).Should().Be(first);
    }

    /// <summary>
    /// Verifies that a bundle built on a commit the repository does not have is refused before anything is unpacked.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Bundle_Built_On_An_Unknown_Commit_Is_Refused()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var origin = new TempDirectory();
        using var workstation = new TempDirectory();
        using var work = new TempDirectory();
        await CreateOriginAsync(origin);
        await CloneAsync(origin.Path, workstation.Path);
        workstation.Write("b.txt", "new\n");
        var second = await CommitAsync(workstation.Path, "second");
        workstation.Write("c.txt", "later\n");
        var third = await CommitAsync(workstation.Path, "third");
        var bundle = Path.Combine(work.Path, "commits.bundle");
        await _service.CreateBundleAsync(workstation.Path, bundle, second, third);

        var unbundle = () => _service.UnbundleAsync(origin.Path, bundle, Path.Combine(work.Path, "objects"));

        await unbundle.Should().ThrowAsync<GitExecutionException>();
    }

    /// <summary>
    /// Verifies that a merge in the block arrives with the commits it brings in, as one link of the first-parent chain.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Merge_Arrives_With_The_Commits_It_Brings_In()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var origin = new TempDirectory();
        using var workstation = new TempDirectory();
        using var work = new TempDirectory();
        var first = await CreateOriginAsync(origin);
        await CloneAsync(origin.Path, workstation.Path);
        (await _runner.RunAsync(new[] { "checkout", "--quiet", "-b", "side" }, workstation.Path)).ExitCode.Should().Be(0);
        workstation.Write("side.txt", "side\n");
        var side = await CommitAsync(workstation.Path, "side");
        (await _runner.RunAsync(new[] { "checkout", "--quiet", "main" }, workstation.Path)).ExitCode.Should().Be(0);
        workstation.Write("main.txt", "main\n");
        var second = await CommitAsync(workstation.Path, "second");
        (await _runner.RunAsync(new[] { "-c", "user.name=t", "-c", "user.email=t@t", "merge", "--quiet", "--no-ff", "-m", "merge side", "side" }, workstation.Path)).ExitCode.Should().Be(0);
        var merge = await HeadAsync(workstation.Path);
        var bundle = Path.Combine(work.Path, "commits.bundle");

        await _service.CreateBundleAsync(workstation.Path, bundle, first, merge);
        var quarantine = await _service.UnbundleAsync(origin.Path, bundle, Path.Combine(work.Path, "objects"));
        var chain = await _service.GetFirstParentChainAsync(origin.Path, first, merge, quarantine);
        var paths = await _service.GetTouchedPathsAsync(origin.Path, first, merge, quarantine);
        _service.MovePacks(quarantine);
        await _service.FastForwardAsync(origin.Path, merge);

        chain.Select(link => link.Hash).Should().Equal(second, merge);
        paths.Should().Contain(new[] { "side.txt", "main.txt" });
        (await HasObjectAsync(origin.Path, side)).Should().BeTrue();
        (await HeadAsync(origin.Path)).Should().Be(merge);
    }

    /// <summary>
    /// Verifies that a Soft rollback moves the branch back and keeps the changes of the dropped commit staged.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Soft_Rollback_Keeps_The_Changes_Staged()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        using var repository = new TempDirectory();
        var first = await CreateOriginAsync(repository);
        repository.Write("b.txt", "new\n");
        await CommitAsync(repository.Path, "second");

        await _service.ResetAsync(repository.Path, first, CommitResetMode.Soft);

        (await HeadAsync(repository.Path)).Should().Be(first);
        (await _runner.RunAsync(new[] { "diff", "--cached", "--name-only" }, repository.Path)).StandardOutput.Trim().Should().Be("b.txt");
    }

    /// <summary>
    /// Verifies that a value that is not a full commit name never reaches Git.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Names_That_Are_Not_Full_Commits_Are_Refused()
    {
        using var work = new TempDirectory();
        var commit = new string('a', 40);

        var bundle = () => _service.CreateBundleAsync(work.Path, Path.Combine(work.Path, "x.bundle"), "--all", commit);
        var list = () => _service.ListFirstParentAsync(work.Path, commit, "HEAD", 10);
        var reset = () => _service.ResetAsync(work.Path, "-q", CommitResetMode.Hard);

        await bundle.Should().ThrowAsync<ArgumentException>();
        await list.Should().ThrowAsync<ArgumentException>();
        await reset.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Creates a repository with one commit on <c>main</c>.
    /// </summary>
    /// <param name="directory">The folder.</param>
    /// <returns>The commit.</returns>
    private async Task<string> CreateOriginAsync(TempDirectory directory)
    {
        (await _runner.RunAsync(new[] { "init", "--quiet", "--initial-branch=main" }, directory.Path)).ExitCode.Should().Be(0);
        directory.Write("a.txt", "one\n");
        return await CommitAsync(directory.Path, "first");
    }

    /// <summary>
    /// Clones a repository.
    /// </summary>
    /// <param name="from">The repository.</param>
    /// <param name="to">The empty folder of the clone.</param>
    /// <returns>A task that completes when the clone exists.</returns>
    private async Task CloneAsync(string from, string to)
        => (await _runner.RunAsync(new[] { "clone", "--quiet", from, to }, null)).ExitCode.Should().Be(0);

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
        return await HeadAsync(directory);
    }

    /// <summary>
    /// Reads the commit checked out.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The full commit name.</returns>
    private async Task<string> HeadAsync(string directory)
        => (await _runner.RunAsync(new[] { "rev-parse", "HEAD" }, directory)).StandardOutput.Trim();

    /// <summary>
    /// Checks whether a repository has an object, without any quarantine.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <param name="commit">The object.</param>
    /// <returns><see langword="true"/> when the repository has it.</returns>
    private async Task<bool> HasObjectAsync(string directory, string commit)
        => (await _runner.RunAsync(new[] { "cat-file", "-e", commit + "^{commit}" }, directory)).ExitCode == 0;

    /// <summary>
    /// Lists every reference of a repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The output of <c>git for-each-ref</c>.</returns>
    private async Task<string> RefsAsync(string directory)
        => (await _runner.RunAsync(new[] { "for-each-ref", "--format=%(refname)" }, directory)).StandardOutput;

    /// <summary>
    /// Lists the pack files of a repository.
    /// </summary>
    /// <param name="directory">The repository.</param>
    /// <returns>The file names.</returns>
    private static string[] PackFiles(string directory)
    {
        var pack = Path.Combine(directory, ".git", "objects", "pack");
        return Directory.Exists(pack) ? Directory.GetFiles(pack).Select(file => Path.GetFileName(file)).ToArray() : Array.Empty<string>();
    }
}
