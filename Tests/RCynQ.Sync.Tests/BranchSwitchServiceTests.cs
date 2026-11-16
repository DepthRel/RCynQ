using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="GitChannel.BranchSwitchService"/> on a real temporary repository of the Workstation. They return
/// early when Git is not installed.
/// </summary>
/// <remarks>
/// The repository has <c>main</c> (also fetched as <c>rcynq/main</c>) with <c>a.txt</c> = <c>v1</c> and <c>b.txt</c>, and
/// a branch of the Origin <c>rcynq/feature</c> on which <c>a.txt</c> = <c>feature</c>.
/// </remarks>
public sealed class BranchSwitchServiceTests : IDisposable
{
    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The Git runner.</summary>
    private readonly GitCommandRunner _git = new(new SystemProcessRunner(TestTexts.English), new GitOptions());

    /// <summary>
    /// Verifies that the list names the current branch, the branch of the Origin and both branches with their status.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_List_Shows_Local_And_Origin_Branches()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        await RunAsync("symbolic-ref", "refs/remotes/rcynq/HEAD", "refs/remotes/rcynq/feature");

        var list = await _fixture.CreateBranchSwitchService().ListAsync();

        list.Current.Should().Be("main");
        list.OriginHead.Should().Be("feature");
        list.Branches.Select(branch => (branch.Name, branch.Status)).Should().Equal(
            ("feature", BranchSyncStatus.NotPulled),
            ("main", BranchSyncStatus.Pulled));
    }

    /// <summary>
    /// Verifies that a working tree with changes is not switched until the request says what happens to them, also when
    /// the request names no mode the service knows.
    /// </summary>
    /// <param name="mode">The mode of the request.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(BranchSwitchMode.IfClean)]
    [InlineData((BranchSwitchMode)0)]
    [InlineData((BranchSwitchMode)42)]
    public async Task A_Dirty_Working_Tree_Waits_For_A_Choice(BranchSwitchMode mode)
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "edited\n");

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest("feature", mode));

        result.Outcome.Should().Be(BranchSwitchOutcome.ChangesInTheWay);
        result.Files.Should().Equal("b.txt");
        (await CurrentBranchAsync()).Should().Be("main");
        SyncFixture.Read(_fixture.WorkstationWork, "b.txt").Should().Be("edited\n");
    }

    /// <summary>
    /// Verifies that carried changes come along, and that a branch of the Origin becomes a local branch that follows it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Carried_Changes_Come_Along_To_A_New_Branch_That_Follows_The_Origin()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "edited\n");

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.CarryChanges));

        result.Outcome.Should().Be(BranchSwitchOutcome.Switched, result.Message);
        result.Message.Should().Contain(TestTexts.English.Format("BranchSwitch.Created", "feature", "rcynq"));
        result.Branch!.Branch.Should().Be("feature");
        result.Branch.TrackingRef.Should().Be("refs/remotes/rcynq/feature");
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("feature\n");
        SyncFixture.Read(_fixture.WorkstationWork, "b.txt").Should().Be("edited\n");
        (await OutputAsync("config", "branch.feature.remote")).Should().Be("rcynq");
    }

    /// <summary>
    /// Verifies that carrying changes the other branch would overwrite is refused, names the file and changes nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Carrying_Colliding_Changes_Is_Refused()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "local\n");

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.CarryChanges));

        result.Outcome.Should().Be(BranchSwitchOutcome.Refused);
        result.Files.Should().Equal("a.txt");
        (await CurrentBranchAsync()).Should().Be("main");
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("local\n");
    }

    /// <summary>
    /// Verifies that saved changes are taken out of the working tree and its index before the switch, and that the patch
    /// brings them back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saved_Changes_Are_Taken_Out_And_Can_Come_Back()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        var service = _fixture.CreateBranchSwitchService();
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "local\n");
        SyncFixture.Write(_fixture.WorkstationWork, "new.txt", "new\n");
        await RunAsync("add", "new.txt");
        var patch = await service.BuildChangesPatchAsync();

        var result = await service.SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.SavePatch, patch));

        result.Outcome.Should().Be(BranchSwitchOutcome.Switched, result.Message);
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("feature\n");
        File.Exists(Path.Combine(_fixture.WorkstationWork, "new.txt")).Should().BeFalse();
        (await OutputAsync("status", "--porcelain")).Should().BeEmpty();

        (await service.SwitchAsync(new SwitchBranchRequest("main", BranchSwitchMode.IfClean))).Outcome.Should().Be(BranchSwitchOutcome.Switched);
        var operation = _fixture.CreatePatchFileOperation(_fixture.WorkstationNode);
        (await operation.ApplyAsync(patch, null, reverse: false)).Succeeded.Should().BeTrue();
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("local\n");
        SyncFixture.Read(_fixture.WorkstationWork, "new.txt").Should().Be("new\n");
    }

    /// <summary>
    /// Verifies that saved changes go back into the working tree when Git then refuses the switch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saved_Changes_Come_Back_When_The_Switch_Fails()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        var branches = Substitute.For<IGitBranchService>();
        branches.ListSwitchableAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new[] { new GitSwitchableBranch("feature", BranchSyncStatus.NotPulled, 0, 0) });
        branches.GetCommitsOnlyInHeadAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<string>());
        branches.SwitchAsync(Arg.Any<string>(), Arg.Any<string>(), "feature", Arg.Any<CancellationToken>())
            .Returns(new GitSwitchAttempt(false, false, Array.Empty<string>(), "fatal: something went wrong"));
        var service = _fixture.CreateBranchSwitchService(branches);
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "local\n");
        var patch = await service.BuildChangesPatchAsync();

        var result = await service.SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.SavePatch, patch));

        result.Outcome.Should().Be(BranchSwitchOutcome.Refused);
        result.Message.Should().Contain(TestTexts.English.Get("BranchSwitch.ChangesRestored"));
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("local\n");
    }

    /// <summary>
    /// Verifies that a patch that no longer fits the working tree stops the switch before anything changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Does_Not_Fit_Stops_The_Switch()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        var service = _fixture.CreateBranchSwitchService();
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "local\n");
        var patch = await service.BuildChangesPatchAsync();
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "edited after saving\n");

        var result = await service.SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.SavePatch, patch));

        result.Outcome.Should().Be(BranchSwitchOutcome.Refused);
        (await CurrentBranchAsync()).Should().Be("main");
        SyncFixture.Read(_fixture.WorkstationWork, "a.txt").Should().Be("edited after saving\n");
    }

    /// <summary>
    /// Verifies that a detached HEAD whose commit no branch holds is never left.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Detached_Head_With_Its_Own_Commits_Is_Not_Left()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        await RunAsync("checkout", "--quiet", "--detach");
        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "detached\n");
        await RunAsync("commit", "--quiet", "-am", "detached");
        var lost = await OutputAsync("rev-parse", "HEAD");

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest("main", BranchSwitchMode.IfClean));

        result.Outcome.Should().Be(BranchSwitchOutcome.Refused);
        result.Message.Should().Contain(lost[..10]);
        (await OutputAsync("rev-parse", "HEAD")).Should().Be(lost);
    }

    /// <summary>
    /// Verifies that leaving a branch with commits the Origin does not have says so.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Leaving_Unsent_Commits_Is_Reported()
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        SyncFixture.Write(_fixture.WorkstationWork, "b.txt", "committed\n");
        await RunAsync("commit", "--quiet", "-am", "unsent");

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest("feature", BranchSwitchMode.IfClean));

        result.Outcome.Should().Be(BranchSwitchOutcome.Switched, result.Message);
        result.Message.Should().Contain(TestTexts.English.Format("BranchSwitch.LeftCommits", "main", 1));
    }

    /// <summary>
    /// Verifies that an unknown or unsafe branch name is refused without touching the working tree.
    /// </summary>
    /// <param name="branch">The name.</param>
    /// <param name="key">The key of the expected message.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("missing", "BranchSwitch.NoSuchBranch")]
    [InlineData("--orphan", "BranchSwitch.InvalidName")]
    public async Task An_Unknown_Or_Unsafe_Branch_Is_Refused(string branch, string key)
    {
        if (!await CreateRepositoryAsync())
        {
            return;
        }

        var result = await _fixture.CreateBranchSwitchService().SwitchAsync(new SwitchBranchRequest(branch, BranchSwitchMode.IfClean));

        result.Outcome.Should().Be(BranchSwitchOutcome.Refused);
        result.Message.Should().Be(TestTexts.English.Format(key, branch));
        (await CurrentBranchAsync()).Should().Be("main");
    }

    /// <summary>
    /// Verifies that only a Workstation switches branches.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Does_Not_Switch_Branches()
    {
        var service = _fixture.CreateBranchSwitchService(node: _fixture.OriginNode);

        var act = () => service.SwitchAsync(new SwitchBranchRequest("main", BranchSwitchMode.IfClean));

        await act.Should().ThrowAsync<SyncBridgeException>();
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Makes the Workstation working tree the repository described on the class, when Git is installed.
    /// </summary>
    /// <returns><see langword="true"/> when the repository is ready.</returns>
    private async Task<bool> CreateRepositoryAsync()
    {
        try
        {
            if ((await _git.RunAsync(new[] { "--version" }, null)).ExitCode != 0)
            {
                return false;
            }
        }
        catch (SyncBridgeException)
        {
            return false;
        }

        await RunAsync("init", "--quiet", "-b", "main");
        await RunAsync("config", "core.autocrlf", "false");
        await RunAsync("config", "user.name", "t");
        await RunAsync("config", "user.email", "t@t");
        await RunAsync("add", "-A");
        await RunAsync("commit", "--quiet", "-m", "base");
        await RunAsync("update-ref", "refs/remotes/rcynq/main", "HEAD");

        await RunAsync("checkout", "--quiet", "-b", "temporary");
        SyncFixture.Write(_fixture.WorkstationWork, "a.txt", "feature\n");
        await RunAsync("commit", "--quiet", "-am", "feature");
        await RunAsync("update-ref", "refs/remotes/rcynq/feature", "HEAD");
        await RunAsync("checkout", "--quiet", "main");
        await RunAsync("branch", "--quiet", "-D", "temporary");
        return true;
    }

    /// <summary>
    /// Gets the branch the Workstation working tree is on.
    /// </summary>
    /// <returns>The branch.</returns>
    private Task<string> CurrentBranchAsync() => OutputAsync("symbolic-ref", "--short", "HEAD");

    /// <summary>
    /// Runs git in the Workstation working tree and fails the test when git fails.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>A task representing the command.</returns>
    private async Task RunAsync(params string[] arguments) => await OutputAsync(arguments);

    /// <summary>
    /// Runs git in the Workstation working tree, fails the test when git fails and returns what it wrote.
    /// </summary>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The trimmed output.</returns>
    private async Task<string> OutputAsync(params string[] arguments)
    {
        var result = await _git.RunAsync(arguments, _fixture.WorkstationWork);
        result.ExitCode.Should().Be(0, $"git {string.Join(' ', arguments)}: {result.StandardError}");
        return result.StandardOutput.Trim();
    }
}
