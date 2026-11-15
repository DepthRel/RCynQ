using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Repository;
using RCynQ.Sync.Partial;
using RCynQ.Sync.Tests.Fixtures;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="ChangeRevertOperation"/>: who may take changes back, what is refused, and what happens to a choice
/// of the changes of the file.
/// </summary>
public sealed class ChangeRevertOperationTests : IDisposable
{
    /// <summary>The file the tests take back.</summary>
    private const string FILE = "c.txt";

    /// <summary>The fixture.</summary>
    private readonly SyncFixture _fixture = new();

    /// <summary>The repository service substitute.</summary>
    private readonly IGitRepositoryService _git = Substitute.For<IGitRepositoryService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="ChangeRevertOperationTests"/> class.
    /// </summary>
    public ChangeRevertOperationTests()
    {
        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(true);
    }

    /// <summary>
    /// Verifies that a revert on a Workstation takes the changes back and forgets the choice made on them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Revert_Forgets_The_Choice_Of_The_File()
    {
        await SaveChoiceAsync();
        _git.RevertWorkingTreeFileAsync(_fixture.WorkstationPaths.Workdir, FILE, Arg.Any<CancellationToken>()).Returns(WorkingTreeRevertOutcome.Restored);

        var result = await Operation(_fixture.WorkstationNode).ExecuteAsync(new RevertChangesRequest(FILE));

        result.Should().Be(new RevertChangesResult(FILE, WorkingTreeRevertOutcome.Restored));
        (await _fixture.WorkstationPartials.GetAsync(FILE)).Should().BeNull();
    }

    /// <summary>
    /// Verifies that a file with only staged changes keeps its choice, since nothing was taken back.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Nothing_To_Revert_Keeps_The_Choice()
    {
        await SaveChoiceAsync();
        _git.RevertWorkingTreeFileAsync(Arg.Any<string>(), FILE, Arg.Any<CancellationToken>()).Returns(WorkingTreeRevertOutcome.NothingToRevert);

        var result = await Operation(_fixture.WorkstationNode).ExecuteAsync(new RevertChangesRequest(FILE));

        result.Outcome.Should().Be(WorkingTreeRevertOutcome.NothingToRevert);
        (await _fixture.WorkstationPartials.GetAsync(FILE)).Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that an Origin refuses: its repository changes only by patches.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Refuses()
    {
        var revert = () => Operation(_fixture.OriginNode).ExecuteAsync(new RevertChangesRequest(FILE));

        await revert.Should().ThrowAsync<SyncBridgeException>().WithMessage(TestTexts.English.Format("Changes.RevertOnlyWorkstation", NodeRole.Origin));
        await _git.DidNotReceiveWithAnyArgs().RevertWorkingTreeFileAsync(default!, default!, default);
    }

    /// <summary>
    /// Verifies that a working tree without Git and a path Git does not know are refused.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task No_Git_And_An_Unknown_Path_Are_Refused()
    {
        _git.RevertWorkingTreeFileAsync(Arg.Any<string>(), FILE, Arg.Any<CancellationToken>()).Returns(WorkingTreeRevertOutcome.Unknown);
        var revert = () => Operation(_fixture.WorkstationNode).ExecuteAsync(new RevertChangesRequest(FILE));

        await revert.Should().ThrowAsync<SyncBridgeException>().WithMessage(TestTexts.English.Format("Changes.CannotRevert", FILE));

        _git.IsWorkTreeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(false);
        await revert.Should().ThrowAsync<SyncBridgeException>().WithMessage(TestTexts.English.Get("Changes.RevertNeedsGit"));
    }

    /// <summary>
    /// Verifies that a blank path or one that leaves the working tree is refused before Git is asked.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData("")]
    [InlineData("../outside.txt")]
    [InlineData(".git/config")]
    public async Task Unsafe_Paths_Are_Refused(string path)
    {
        var revert = () => Operation(_fixture.WorkstationNode).ExecuteAsync(new RevertChangesRequest(path));

        await revert.Should().ThrowAsync<ArgumentException>();
        await _git.DidNotReceiveWithAnyArgs().RevertWorkingTreeFileAsync(default!, default!, default);
    }

    /// <inheritdoc />
    public void Dispose() => _fixture.Dispose();

    /// <summary>
    /// Creates the operation of a node.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The operation.</returns>
    private ChangeRevertOperation Operation(NodeConfig node)
        => new(
            TestTexts.English,
            node,
            node.Id == _fixture.WorkstationNode.Id ? _fixture.WorkstationPaths : _fixture.OriginPaths,
            _fixture.Paths,
            _git,
            _fixture.WorkstationPartials,
            _fixture.LockOf(node),
            _fixture.CreateTracker(node));

    /// <summary>
    /// Stores a choice of the changes of the file.
    /// </summary>
    /// <returns>A task that completes when the choice is stored.</returns>
    private Task SaveChoiceAsync()
        => _fixture.WorkstationPartials.SaveAsync(new PartialChange(
            _fixture.WorkstationPartials.RepositoryId,
            FILE,
            "base",
            "work",
            "chosen\n"u8.ToArray(),
            new[] { 0 },
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));
}
