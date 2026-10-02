using FluentAssertions;
using RCynQ.Core.Enums;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Git.Tests.Support;

namespace RCynQ.Git.Tests;

/// <summary>
/// Integration tests for the operations a Pull and the sync base rely on, run against the real <c>git</c> executable.
/// They return early when git is not installed.
/// </summary>
public sealed class GitRepositoryServicePullSupportTests : IDisposable
{
    /// <summary>The repository under test.</summary>
    private readonly TempDirectory _repo = new();

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>The service under test.</summary>
    private readonly GitRepositoryService _service;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitRepositoryServicePullSupportTests"/> class.
    /// </summary>
    public GitRepositoryServicePullSupportTests()
    {
        _service = new GitRepositoryService(TestTexts.English, _git);
    }

    /// <summary>
    /// Verifies that modified, deleted and untracked files are reported as dirty, and clean or ignored ones are not.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Dirty_Paths_Cover_Modified_Deleted_And_Untracked_Files()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CreateRepositoryAsync();
        _repo.Write("a.txt", "changed\n");
        File.Delete(_repo["sub/b.txt"]);
        _repo.Write("new dir/new.txt", "new\n");
        _repo.Write("ignored.log", "noise\n");

        var dirty = await _service.GetDirtyPathsAsync(_repo.Path);

        dirty.Should().BeEquivalentTo("a.txt", "sub/b.txt", "new dir/new.txt");
    }

    /// <summary>
    /// Verifies the comparison of working tree files with a commit, including files missing on either side.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Matches_Commit_Compares_Content_And_Absence()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CreateRepositoryAsync();

        (await _service.MatchesCommitAsync(_repo.Path, commit, "a.txt")).Should().BeTrue();
        _repo.Write("a.txt", "changed\n");
        (await _service.MatchesCommitAsync(_repo.Path, commit, "a.txt")).Should().BeFalse();
        (await _service.MatchesCommitAsync(_repo.Path, commit, "missing.txt")).Should().BeTrue("absent on both sides");
        _repo.Write("missing.txt", "now here\n");
        (await _service.MatchesCommitAsync(_repo.Path, commit, "missing.txt")).Should().BeFalse();
        File.Delete(_repo["sub/b.txt"]);
        (await _service.MatchesCommitAsync(_repo.Path, commit, "sub/b.txt")).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that discarding restores tracked files, removes untracked ones and treats pattern characters literally.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Discard_Restores_Tracked_And_Removes_Untracked_Files()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CreateRepositoryAsync();
        _repo.Write("a.txt", "changed\n");
        _repo.Write("[x].txt", "untracked with pattern characters\n");
        _repo.Write("x.txt", "must survive: '[x].txt' is not a pattern\n");

        await _service.DiscardLocalChangesAsync(_repo.Path, new[] { "a.txt", "[x].txt" });

        File.ReadAllText(_repo["a.txt"]).Replace("\r\n", "\n", StringComparison.Ordinal).Should().Be("v1\n");
        File.Exists(_repo["[x].txt"]).Should().BeFalse();
        File.Exists(_repo["x.txt"]).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that paths leaving the working tree or pointing into Git metadata are refused.
    /// </summary>
    /// <param name="path">The unsafe path.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    [InlineData("-rf")]
    public async Task Discard_Refuses_Unsafe_Paths(string path)
    {
        var discard = () => _service.DiscardLocalChangesAsync(_repo.Path, new[] { path });

        await discard.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Verifies the common ancestor and the fast-forward check.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Merge_Base_And_Ancestry()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var first = await CreateRepositoryAsync();
        _repo.Write("a.txt", "v2\n");
        await RunAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-am", "second");
        var second = await _service.GetHeadCommitAsync(_repo.Path);

        (await _service.GetMergeBaseAsync(_repo.Path, first, second!)).Should().Be(first);
        (await _service.IsAncestorAsync(_repo.Path, first, second!)).Should().BeTrue();
        (await _service.IsAncestorAsync(_repo.Path, second!, first)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the checked out branch of a remote repository is read, and that a repository RCynQ creates keeps the user's
    /// own line-ending settings, like a clone.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Remote_Head_Is_Read_And_New_Repositories_Keep_The_User_Settings()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await CreateRepositoryAsync();
        await RunAsync("checkout", "--quiet", "-b", "develop");
        using var clone = new TempDirectory();
        var url = new Uri(_repo.Path).AbsoluteUri;

        (await _service.GetRemoteHeadBranchAsync(clone.Path, url, null)).Should().Be("develop");

        await _service.InitializeAsync(clone.Path, "develop");
        var autocrlf = await _git.RunAsync(new[] { "config", "--local", "--get", "core.autocrlf" }, clone.Path);
        autocrlf.StandardOutput.Trim().Should().BeEmpty("no line-ending setting is forced on the repository");
    }

    /// <summary>
    /// Verifies that the export writes files exactly as a checkout of the repository does, including line-ending conversion,
    /// and ignores the archive-only <c>export-ignore</c> attribute.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Writes_What_A_Checkout_Writes()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        await RunAsync("init", "--quiet", "--initial-branch=main");
        await RunAsync("config", "core.autocrlf", "true");
        _repo.Write("a.txt", "line1\nline2\n");
        _repo.Write("hidden.txt", "kept by a checkout\n");
        _repo.Write(".gitattributes", "hidden.txt export-ignore\n");
        await RunAsync("add", "-A");
        await RunAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        using var export = new TempDirectory();

        await _service.ExportTreeAsync(_repo.Path, "HEAD", export.Path);

        File.ReadAllText(export["a.txt"]).Should().Be("line1\r\nline2\r\n");
        File.Exists(export["hidden.txt"]).Should().BeTrue();
        Directory.Exists(export[".git"]).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that only files Git itself sees as changed are listed, however their line endings are written.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Changed_Since_Lists_What_Git_Sees_As_Changed()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CreateRepositoryAsync();
        await RunAsync("config", "core.autocrlf", "true");
        _repo.Write("a.txt", "v1\r\n");
        _repo.Write("sub/b.txt", "v2\n");
        _repo.Write("new.txt", "new\n");
        _repo.Write("ignored.log", "noise\n");

        var changed = await _service.GetChangedSinceAsync(_repo.Path, commit);

        changed.Should().BeEquivalentTo("sub/b.txt", "new.txt");
    }

    /// <summary>
    /// Verifies that the changed files come with how each differs from the commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Changed_Files_Since_Tell_How_Each_Differs()
    {
        if (!await GitIntegrationTests.GitAvailableAsync())
        {
            return;
        }

        var commit = await CreateRepositoryAsync();
        _repo.Write("a.txt", "v2\n");
        File.Delete(_repo.Path + "/sub/b.txt");
        _repo.Write("new.txt", "new\n");
        _repo.Write("ignored.log", "noise\n");

        var changed = await _service.GetChangedFilesSinceAsync(_repo.Path, commit);

        changed.Should().BeEquivalentTo(new Dictionary<string, ChangeKind>
        {
            ["a.txt"] = ChangeKind.Modified,
            ["sub/b.txt"] = ChangeKind.Deleted,
            ["new.txt"] = ChangeKind.Added
        });
    }

    /// <inheritdoc />
    public void Dispose() => _repo.Dispose();

    /// <summary>
    /// Creates the repository with <c>a.txt</c>, <c>sub/b.txt</c> and a <c>.gitignore</c> for <c>*.log</c>.
    /// </summary>
    /// <returns>The commit.</returns>
    private async Task<string> CreateRepositoryAsync()
    {
        await RunAsync("init", "--quiet", "--initial-branch=main");
        _repo.Write("a.txt", "v1\n");
        _repo.Write("sub/b.txt", "v1\n");
        _repo.Write(".gitignore", "*.log\n");
        await RunAsync("add", "-A");
        await RunAsync("-c", "user.name=t", "-c", "user.email=t@t", "commit", "--quiet", "-m", "first");
        return (await _service.GetHeadCommitAsync(_repo.Path))!;
    }

    /// <summary>
    /// Runs git in the repository and asserts success.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RunAsync(params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, _repo.Path);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
    }
}
