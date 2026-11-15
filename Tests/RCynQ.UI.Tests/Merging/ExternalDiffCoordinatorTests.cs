using System.Text;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.MergeTools;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Merging;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of <see cref="ExternalDiffCoordinator"/>: the diff of a changed file in a merge tool and a choice made there.
/// </summary>
public sealed class ExternalDiffCoordinatorTests : IDisposable
{
    /// <summary>The folder of the files the service prepares.</summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rcynq-diff-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The launcher substitute.</summary>
    private readonly IMergeToolLauncher _launcher = Substitute.For<IMergeToolLauncher>();

    /// <summary>The tool.</summary>
    private readonly MergeTool _tool = new("vscode", "VS Code", "code", new[] { "{local}", "{incoming}", "{result}" }, false, new[] { "{left}", "{right}" });

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalDiffCoordinatorTests"/> class.
    /// </summary>
    public ExternalDiffCoordinatorTests()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, "base.txt"), "1\n");
        File.WriteAllText(Path.Combine(_folder, "choice.txt"), "1\n2\n3\n");
        _api.PrepareCompareAsync("a.txt", Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new CompareFilesResponse("a.txt", DiffAvailability.Text, Path.Combine(_folder, "base.txt"), Path.Combine(_folder, "choice.txt"), "b", "w"));
    }

    /// <summary>
    /// Verifies that a diff to look at starts the tool with the copy of the base on the left and does not wait for it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Show_Starts_The_Tool_Without_Waiting()
    {
        var process = Substitute.For<IRunningProcess>();
        _launcher.StartDiff(_tool, Arg.Any<DiffToolFiles>(), Arg.Any<DiffToolLabels>()).Returns(process);

        await Coordinator().ShowAsync("a.txt", _tool, NodeRole.Origin);

        _launcher.Received(1).StartDiff(_tool, new DiffToolFiles(Path.Combine(_folder, "base.txt"), Path.Combine(_folder, "choice.txt")), Arg.Is<DiffToolLabels>(labels => labels.Left.Contains("a.txt") && labels.Left.Contains("Origin")));
        await _api.Received(1).PrepareCompareAsync("a.txt", false, Arg.Any<CancellationToken>());
        await process.DidNotReceiveWithAnyArgs().WaitForExitAsync(default);
    }

    /// <summary>
    /// Verifies that what the user left in the right pane becomes the choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Choose_Keeps_What_Was_Left_In_The_Tool()
    {
        StartEditing("1\n2\n");
        _api.SetPartialContentAsync(Arg.Any<SetPartialContentRequest>(), Arg.Any<CancellationToken>()).Returns(new PartialChoiceResult("a.txt", PartialChoiceOutcome.Partial));

        var result = await Coordinator().ChooseAsync("a.txt", _tool, NodeRole.Origin, () => Task.FromResult(false));

        result!.Outcome.Should().Be(PartialChoiceOutcome.Partial);
        await _api.Received(1).SetPartialContentAsync(
            Arg.Is<SetPartialContentRequest>(request => request.Path == "a.txt" && request.BaseSha256 == "b" && request.WorkdirSha256 == "w" && Encoding.UTF8.GetString(request.Content) == "1\n2\n"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a copy the tool did not change is not sent, and that the user is asked whether to look again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Unchanged_Copy_Asks_And_Sends_Nothing()
    {
        StartEditing(null);
        var asked = 0;

        var result = await Coordinator().ChooseAsync("a.txt", _tool, NodeRole.Origin, () => Task.FromResult(++asked < 2));

        result.Should().BeNull();
        asked.Should().Be(2);
        _launcher.ReceivedWithAnyArgs(2).StartDiff(default!, default!, default!);
        await _api.DidNotReceiveWithAnyArgs().SetPartialContentAsync(default!, default);
    }

    /// <summary>
    /// Verifies that a copy saved while the question was open is kept, even when the user declines to open the tool again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Copy_Saved_During_The_Question_Is_Kept()
    {
        StartEditing(null);
        _api.SetPartialContentAsync(Arg.Any<SetPartialContentRequest>(), Arg.Any<CancellationToken>()).Returns(new PartialChoiceResult("a.txt", PartialChoiceOutcome.Partial));

        var result = await Coordinator().ChooseAsync("a.txt", _tool, NodeRole.Origin, () =>
        {
            File.WriteAllText(Path.Combine(_folder, "choice.txt"), "1\n3\n");
            return Task.FromResult(false);
        });

        result!.Outcome.Should().Be(PartialChoiceOutcome.Partial);
        await _api.Received(1).SetPartialContentAsync(Arg.Is<SetPartialContentRequest>(request => Encoding.UTF8.GetString(request.Content) == "1\n3\n"), Arg.Any<CancellationToken>());
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    /// <summary>
    /// Makes the tool write the right pane when the coordinator waits for it.
    /// </summary>
    /// <param name="content">What the user leaves, or <see langword="null"/> to leave the copy as it was.</param>
    private void StartEditing(string? content)
    {
        var process = Substitute.For<IRunningProcess>();
        process.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (content is not null)
            {
                File.WriteAllText(Path.Combine(_folder, "choice.txt"), content);
            }

            return Task.CompletedTask;
        });
        _launcher.StartDiff(_tool, Arg.Any<DiffToolFiles>(), Arg.Any<DiffToolLabels>()).Returns(process);
    }

    /// <summary>
    /// Creates the coordinator.
    /// </summary>
    /// <returns>The coordinator.</returns>
    private ExternalDiffCoordinator Coordinator() => new(TestTexts.English, _api, _launcher);
}
