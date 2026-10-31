using System.Net;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.MergeTools;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Merging;

namespace RCynQ.UI.Tests.Merging;

/// <summary>
/// Tests of a merge in a merge tool, with the service and the tool replaced: the "tool" writes the result file, or does
/// not, and the user answers the questions from a list.
/// </summary>
public sealed class ExternalMergeCoordinatorTests : IDisposable
{
    /// <summary>What Git leaves in the result before the tool: the colliding lines between markers.</summary>
    private const string WITH_MARKERS = "a\n<<<<<<< local\nmine\n=======\ntheirs\n>>>>>>> incoming\nb\n";

    /// <summary>What the user merges the file to.</summary>
    private const string MERGED = "a\nboth\nb\n";

    /// <summary>The patch.</summary>
    private static readonly Guid _patchId = Guid.NewGuid();

    /// <summary>The session.</summary>
    private static readonly Guid _sessionId = Guid.NewGuid();

    /// <summary>The service.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>Starts the tool.</summary>
    private readonly IMergeToolLauncher _launcher = Substitute.For<IMergeToolLauncher>();

    /// <summary>The running tool; it has exited with code 0 unless a test says otherwise.</summary>
    private readonly IRunningProcess _process = Substitute.For<IRunningProcess>();

    /// <summary>The folder of the files of the session.</summary>
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "rcynq-merge-tests", Guid.NewGuid().ToString("N"));

    /// <summary>The merge tool.</summary>
    private readonly MergeTool _tool = new("vscode", "Visual Studio Code", @"C:\Code.exe", new[] { "{local}", "{incoming}", "{result}" }, TrustExitCode: false);

    /// <summary>The file of the session.</summary>
    private readonly MergeSessionFile _file;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExternalMergeCoordinatorTests"/> class: a session with one file whose
    /// result holds Git's markers.
    /// </summary>
    public ExternalMergeCoordinatorTests()
    {
        Directory.CreateDirectory(_folder);
        _file = new MergeSessionFile("src/app.cs", Write("app.local.cs", "mine"), Write("app.base.cs", "base"), Write("app.incoming.cs", "theirs"), Write("app.cs", WITH_MARKERS));
        _api.PrepareMergeAsync(_patchId, Arg.Any<CancellationToken>())
            .Returns(new MergeSessionResponse(_sessionId, _patchId, 2, new[] { _file }, Array.Empty<ConflictFile>()));
        _api.CompleteMergeAsync(_sessionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new PatchApplyResult(_patchId, PatchStatus.ConflictResolved, 3, Array.Empty<ConflictFile>(), null));
        _process.ExitCode.Returns(0);
        _process.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
    }

    /// <summary>
    /// Verifies the reported case: the file opens in the tool with its versions and titles, the user merges it, and the
    /// service applies the merge under the name of the tool, without a question.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Merged_File_Is_Applied()
    {
        ToolWrites(MERGED);
        var progress = new ScriptedProgress();

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        outcome.Result!.Status.Should().Be(PatchStatus.ConflictResolved);
        outcome.FilesWithMarkers.Should().BeEmpty();
        progress.Questions.Should().BeEmpty();
        progress.Files.Should().Equal("1/1 src/app.cs");
        _launcher.Received(1).Start(
            _tool,
            Arg.Is<MergeToolFiles>(files => files.Local == _file.LocalFile && files.Base == _file.BaseFile && files.Incoming == _file.IncomingFile && files.Result == _file.ResultFile),
            Arg.Is<MergeToolLabels>(labels => labels.Local == "Локальная" && labels.Result.Contains("app.cs")));
        await _api.Received(1).CompleteMergeAsync(_sessionId, "Visual Studio Code", Arg.Any<CancellationToken>());
        _process.Received(1).Dispose();
    }

    /// <summary>
    /// Verifies that a patch with files no tool can merge opens nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Files_No_Tool_Can_Merge_Open_Nothing()
    {
        var binary = new ConflictFile("logo.png", null, null);
        _api.PrepareMergeAsync(_patchId, Arg.Any<CancellationToken>())
            .Returns(new MergeSessionResponse(null, _patchId, 0, Array.Empty<MergeSessionFile>(), new[] { binary }));

        var outcome = await Coordinator().RunAsync(_patchId, _tool, new ScriptedProgress());

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Unmergeable);
        outcome.Unmergeable.Should().Equal(binary);
        _launcher.DidNotReceiveWithAnyArgs().Start(default!, default!, default!);
    }

    /// <summary>
    /// Verifies that a tool that returned before the file was saved (a tool that does not wait) is followed by a look
    /// at the file once the user says it is saved, without opening the tool again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_Saved_After_The_Tool_Returned_Is_Taken_On_A_Second_Look()
    {
        _launcher.Start(default!, default!, default!).ReturnsForAnyArgs(_process);
        var progress = new ScriptedProgress(() =>
        {
            File.WriteAllText(_file.ResultFile, MERGED);
            return MergePromptChoice.CheckAgain;
        });

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        progress.Questions.Should().ContainSingle().Which.Should().Contain("src/app.cs");
        progress.Offers.Single().Should().Equal(MergePromptChoice.CheckAgain, MergePromptChoice.OpenAgain, MergePromptChoice.Cancel);
        _launcher.ReceivedWithAnyArgs(1).Start(default!, default!, default!);
    }

    /// <summary>
    /// Verifies that a tool that reports giving the merge up by its exit code is asked about, even if the file changed,
    /// for a tool whose exit code means that.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Trusted_Exit_Code_Means_Not_Saved()
    {
        ToolWrites(MERGED);
        _process.ExitCode.Returns(1);
        var progress = new ScriptedProgress(() => MergePromptChoice.Cancel);

        var outcome = await Coordinator().RunAsync(_patchId, _tool with { TrustExitCode = true }, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Cancelled);
        progress.Questions.Should().ContainSingle();
        await _api.Received(1).CancelMergeAsync(_sessionId, Arg.Any<CancellationToken>());
        await _api.DidNotReceiveWithAnyArgs().CompleteMergeAsync(default, default!, default);
    }

    /// <summary>
    /// Verifies that markers left in the file are asked about, and that the user can take the file with them.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Markers_Left_In_The_File_Can_Be_Kept()
    {
        ToolWrites(WITH_MARKERS + "edited\n");
        var progress = new ScriptedProgress(() => MergePromptChoice.KeepMarkers);

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        outcome.FilesWithMarkers.Should().Equal("src/app.cs");
        progress.Offers.Single().Should().Equal(MergePromptChoice.OpenAgain, MergePromptChoice.KeepMarkers, MergePromptChoice.Cancel);
    }

    /// <summary>
    /// Verifies that a line of equals signs alone, a heading in Markdown, is no conflict marker.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Markdown_Heading_Is_No_Marker()
    {
        ToolWrites("Title\n=======\ntext\n");
        var progress = new ScriptedProgress();

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        progress.Questions.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a tool that cannot start is offered again, and the merge goes on once it starts.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Tool_That_Did_Not_Start_Can_Be_Tried_Again()
    {
        var attempts = 0;
        _launcher.Start(default!, default!, default!).ReturnsForAnyArgs(_ =>
        {
            if (attempts++ == 0)
            {
                throw new SyncBridgeException("Code.exe is gone");
            }

            File.WriteAllText(_file.ResultFile, MERGED);
            return _process;
        });
        var progress = new ScriptedProgress(() => MergePromptChoice.Retry);

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        progress.Questions.Single().Should().Contain("Code.exe is gone");
        attempts.Should().Be(2);
    }

    /// <summary>
    /// Verifies that a merge the service refuses because a local file changed ends with the reason of the service.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Merge_The_Service_Refuses_Ends_With_Its_Reason()
    {
        ToolWrites(MERGED);
        _api.CompleteMergeAsync(_sessionId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceApiException(HttpStatusCode.Conflict, "src/app.cs changed while it was being merged"));

        var outcome = await Coordinator().RunAsync(_patchId, _tool, new ScriptedProgress());

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Failed);
        outcome.Message.Should().Contain("changed while it was being merged");
    }

    /// <summary>
    /// Verifies that a write that failed on the service can be tried again without merging again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Failed_Write_Can_Be_Tried_Again()
    {
        ToolWrites(MERGED);
        _api.CompleteMergeAsync(_sessionId, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<PatchApplyResult>(new ServiceApiException(HttpStatusCode.BadRequest, "app.cs is locked")),
            _ => Task.FromResult(new PatchApplyResult(_patchId, PatchStatus.ConflictResolved, 3, Array.Empty<ConflictFile>(), null)));
        var progress = new ScriptedProgress(() => MergePromptChoice.Retry);

        var outcome = await Coordinator().RunAsync(_patchId, _tool, progress);

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Completed);
        _launcher.ReceivedWithAnyArgs(1).Start(default!, default!, default!);
        await _api.Received(2).CompleteMergeAsync(_sessionId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that giving the merge up while the tool is open stops waiting without closing the tool, and ends the
    /// session on the service.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Giving_Up_While_The_Tool_Is_Open_Leaves_The_Tool_Alone()
    {
        using var cancel = new CancellationTokenSource();
        _launcher.Start(default!, default!, default!).ReturnsForAnyArgs(_process);
        _process.WaitForExitAsync(Arg.Any<CancellationToken>()).Returns(call => Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>()));

        var run = Coordinator().RunAsync(_patchId, _tool, new ScriptedProgress(), cancel.Token);
        await cancel.CancelAsync();
        var outcome = await run;

        outcome.Kind.Should().Be(ExternalMergeOutcomeKind.Cancelled);
        await _api.Received(1).CancelMergeAsync(_sessionId, Arg.Any<CancellationToken>());
        _process.DidNotReceive().Kill();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Makes the tool write the result when it starts.
    /// </summary>
    /// <param name="content">What it writes.</param>
    private void ToolWrites(string content)
        => _launcher.Start(default!, default!, default!).ReturnsForAnyArgs(_ =>
        {
            File.WriteAllText(_file.ResultFile, content);
            return _process;
        });

    /// <summary>
    /// Writes a file of the session.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="content">The content.</param>
    /// <returns>The full path.</returns>
    private string Write(string name, string content)
    {
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>
    /// Creates the coordinator.
    /// </summary>
    /// <returns>The coordinator.</returns>
    private ExternalMergeCoordinator Coordinator() => new(TestTexts.Russian, _api, _launcher);

    /// <summary>
    /// A progress that records what it is shown and answers the questions from a script.
    /// </summary>
    private sealed class ScriptedProgress : IMergeProgress
    {
        /// <summary>The answers, in order.</summary>
        private readonly Queue<Func<MergePromptChoice>> _answers;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScriptedProgress"/> class.
        /// </summary>
        /// <param name="answers">The answers, in order; a question without one gives the merge up.</param>
        public ScriptedProgress(params Func<MergePromptChoice>[] answers)
        {
            _answers = new Queue<Func<MergePromptChoice>>(answers);
        }

        /// <summary>Gets the files shown, as "number/count path".</summary>
        public List<string> Files { get; } = new();

        /// <summary>Gets the questions asked.</summary>
        public List<string> Questions { get; } = new();

        /// <summary>Gets the answers offered for each question.</summary>
        public List<IReadOnlyList<MergePromptChoice>> Offers { get; } = new();

        /// <inheritdoc />
        public void ShowFile(int number, int count, string path) => Files.Add($"{number}/{count} {path}");

        /// <inheritdoc />
        public void ShowStatus(string text)
        {
            // The status line is not part of what these tests look at.
        }

        /// <inheritdoc />
        public Task<MergePromptChoice> AskAsync(string message, IReadOnlyList<MergePromptChoice> choices)
        {
            Questions.Add(message);
            Offers.Add(choices);
            return Task.FromResult(_answers.Count > 0 ? _answers.Dequeue()() : MergePromptChoice.Cancel);
        }
    }
}
