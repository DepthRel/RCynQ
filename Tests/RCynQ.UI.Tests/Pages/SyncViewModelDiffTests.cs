using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.MergeTools;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Merging;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of the diff of a changed file on <see cref="SyncViewModel"/> and of the choice of its changes.
/// </summary>
public sealed class SyncViewModelDiffTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncViewModelDiffTests"/> class.
    /// </summary>
    public SyncViewModelDiffTests()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false), new ChangedFile(ChangeKind.Modified, "b.txt", false));
        _api.GetUnsentCommitsAsync(Arg.Any<CancellationToken>()).Returns(TestData.UnsentCommits());
        _api.GetFileDiffAsync("a.txt", Arg.Any<CancellationToken>()).Returns(Diff());
    }

    /// <summary>
    /// Verifies that the diff opens in the window of RCynQ and that the changes left checked go.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Changes_Left_Checked_Go()
    {
        _dialogs.ShowFileDiffAsync(Arg.Do<FileDiffDialogViewModel>(dialog => dialog.Blocks[1].IsSelected = false)).Returns(true);
        _api.ChooseChangesAsync(Arg.Any<ChooseChangesRequest>(), Arg.Any<CancellationToken>()).Returns(new PartialChoiceResult("a.txt", PartialChoiceOutcome.Partial));
        var sync = await LoadAsync();

        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        await _api.Received(1).ChooseChangesAsync(
            Arg.Is<ChooseChangesRequest>(request => request.Path == "a.txt" && request.BaseSha256 == "base" && request.WorkdirSha256 == "work" && request.Blocks.SequenceEqual(new[] { 0 })),
            Arg.Any<CancellationToken>());
        sync.InfoMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the window of a file left out of the patch opens with no change checked, and that checking one
    /// puts the check mark of the file back on.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Checking_A_Change_Of_A_File_Left_Out_Takes_It_In()
    {
        FileDiffDialogViewModel? shown = null;
        var checkedAtStart = -1;
        _dialogs.ShowFileDiffAsync(Arg.Do<FileDiffDialogViewModel>(dialog =>
        {
            shown = dialog;
            checkedAtStart = dialog.ChosenBlocks.Count;
            dialog.Blocks[0].IsSelected = true;
        })).Returns(true);
        _api.ChooseChangesAsync(Arg.Any<ChooseChangesRequest>(), Arg.Any<CancellationToken>()).Returns(new PartialChoiceResult("a.txt", PartialChoiceOutcome.Partial));
        var sync = await LoadAsync();
        sync.ToggleSelectionModeCommand.Execute(null);
        sync.ChangedFiles[0].IsSelected = false;

        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        checkedAtStart.Should().Be(0);
        shown!.HasNotice.Should().BeFalse();
        await _api.Received(1).ChooseChangesAsync(Arg.Is<ChooseChangesRequest>(request => request.Blocks.SequenceEqual(new[] { 0 })), Arg.Any<CancellationToken>());
        sync.ChangedFiles.Where(file => file.IsSelected).Select(file => file.Path).Should().Equal("a.txt");
        sync.InfoMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies that the window of a file left out of the patch, closed with nothing checked, keeps the file out and
    /// sends no choice.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_Left_Out_Stays_Out_With_Nothing_Checked()
    {
        _dialogs.ShowFileDiffAsync(Arg.Any<FileDiffDialogViewModel>()).Returns(true);
        var sync = await LoadAsync();
        sync.ToggleSelectionModeCommand.Execute(null);
        sync.ChangedFiles[0].IsSelected = false;

        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().ChooseChangesAsync(default!, default);
        sync.ChangedFiles[0].IsSelected.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a window closed without a new choice changes nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Diff_Only_Looked_At_Changes_Nothing()
    {
        _dialogs.ShowFileDiffAsync(Arg.Any<FileDiffDialogViewModel>()).Returns(true);
        var sync = await LoadAsync();

        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().ChooseChangesAsync(default!, default);
    }

    /// <summary>
    /// Verifies that a file with no change chosen is left out of the next patch: check marks turn on with the other files checked.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Nothing_Chosen_Leaves_The_File_Out()
    {
        _dialogs.ShowFileDiffAsync(Arg.Do<FileDiffDialogViewModel>(dialog => dialog.SelectNoneCommand.Execute(null))).Returns(true);
        _api.ChooseChangesAsync(Arg.Any<ChooseChangesRequest>(), Arg.Any<CancellationToken>()).Returns(new PartialChoiceResult("a.txt", PartialChoiceOutcome.Nothing));
        var sync = await LoadAsync();

        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        sync.IsSelectionMode.Should().BeTrue();
        sync.ChangedFiles.Where(file => file.IsSelected).Select(file => file.Path).Should().Equal("b.txt");
        sync.InfoMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a file sent in part is marked and can go whole again, quietly.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Partial_File_Is_Marked_And_Can_Go_Whole()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false, PartialChangeState.Outdated));
        var sync = await LoadAsync();
        var file = sync.ChangedFiles.Single();

        file.IsPartial.Should().BeTrue();
        file.IsPartialOutdated.Should().BeTrue();
        await file.ChooseAllChangesCommand.ExecuteAsync(null);

        await _api.Received(1).ResetPartialAsync("a.txt", Arg.Any<CancellationToken>());
        sync.InfoMessage.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a choice the service dropped is reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Reset_Choice_Is_Reported()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { ChangedFiles = Array.Empty<ChangedFile>(), ResetPartials = new[] { "a.txt" } });

        var sync = await LoadAsync();

        sync.InfoMessage.Should().Be(TestTexts.Russian.Format("Sync.PartialsReset", "a.txt", NodeRole.Origin));
    }

    /// <summary>
    /// Verifies that with the merge tool chosen in the settings the diff opens there, and that a tool that shows no diff
    /// falls back to the window of RCynQ.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Merge_Tool_Of_The_Settings_Shows_The_Diff()
    {
        var launcher = Substitute.For<IMergeToolLauncher>();
        launcher.StartDiff(Arg.Any<MergeTool>(), Arg.Any<DiffToolFiles>(), Arg.Any<DiffToolLabels>()).Returns(Substitute.For<IRunningProcess>());
        var tools = Substitute.For<IMergeToolPreferences>();
        var view = Substitute.For<IChangesViewPreferences>();
        view.Mode.Returns(ChangesViewMode.ExternalTool);
        _api.PrepareCompareAsync("a.txt", false, Arg.Any<CancellationToken>()).Returns(new CompareFilesResponse("a.txt", DiffAvailability.Text, "base/a.txt", "work/a.txt", "base", "work"));
        var sync = new SyncViewModel(TestTexts.Russian, _api, _dialogs, null, tools, null, view, new ExternalDiffCoordinator(TestTexts.Russian, _api, launcher));
        await sync.RefreshAsync();

        tools.GetDefaultAsync(Arg.Any<CancellationToken>()).Returns(new MergeTool("vscode", "VS Code", "code", new[] { "{local}", "{incoming}", "{result}" }, false, new[] { "{left}", "{right}" }));
        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        launcher.Received(1).StartDiff(Arg.Any<MergeTool>(), new DiffToolFiles("base/a.txt", "work/a.txt"), Arg.Any<DiffToolLabels>());
        await _dialogs.DidNotReceiveWithAnyArgs().ShowFileDiffAsync(default!);

        tools.GetDefaultAsync(Arg.Any<CancellationToken>()).Returns(new MergeTool("custom", "merge", "merge", new[] { "{local}", "{incoming}", "{result}" }, false));
        await sync.ChangedFiles[0].ShowDiffCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ShowFileDiffAsync(Arg.Any<FileDiffDialogViewModel>());
        sync.InfoMessage.Should().Be(TestTexts.Russian.Get("Sync.NoDiffTool"));
    }

    /// <summary>
    /// Verifies that the peer is named by its role: the Workstation on an Origin.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task On_An_Origin_The_Changes_Go_To_The_Workstation()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status(NodeRole.Origin) with { ChangedFiles = Array.Empty<ChangedFile>(), ResetPartials = new[] { "a.txt" } });

        var sync = await LoadAsync();

        sync.InfoMessage.Should().Be(TestTexts.Russian.Format("Sync.PartialsReset", "a.txt", NodeRole.Workstation));
    }

    /// <summary>
    /// Verifies that taking the changes of a file back asks first, and that Cancel changes nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Revert_Given_Up_Changes_Nothing()
    {
        DangerConfirmDialogViewModel? asked = null;
        _dialogs.ConfirmDangerAsync(Arg.Do<DangerConfirmDialogViewModel>(dialog => asked = dialog)).Returns(false);
        var sync = await LoadAsync();

        sync.ChangedFiles[0].RevertChangesCommand.CanExecute(null).Should().BeTrue();
        await sync.ChangedFiles[0].RevertChangesCommand.ExecuteAsync(null);

        asked.Should().NotBeNull();
        asked!.Message.Should().Be(TestTexts.Russian.Format("Sync.RevertMessage", "a.txt"));
        asked.Warning.Should().Be(TestTexts.Russian.Get("Sync.RevertIrreversible"));
        await _api.DidNotReceiveWithAnyArgs().RevertChangesAsync(default!, default);
    }

    /// <summary>
    /// Verifies that a confirmed revert asks the service and tells what it came to.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Confirmed_Revert_Is_Done_And_Reported()
    {
        _dialogs.ConfirmDangerAsync(Arg.Any<DangerConfirmDialogViewModel>()).Returns(true);
        _api.RevertChangesAsync("a.txt", Arg.Any<CancellationToken>()).Returns(new RevertChangesResult("a.txt", WorkingTreeRevertOutcome.Deleted));
        var sync = await LoadAsync();

        await sync.ChangedFiles[0].RevertChangesCommand.ExecuteAsync(null);

        await _api.Received(1).RevertChangesAsync("a.txt", Arg.Any<CancellationToken>());
        sync.InfoMessage.Should().Be(TestTexts.Russian.Format("Sync.RevertDeleted", "a.txt"));
    }

    /// <summary>
    /// Verifies that an Origin offers no revert: its repository changes only by patches.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Offers_No_Revert()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status(NodeRole.Origin) with { ChangedFiles = new[] { new ChangedFile(ChangeKind.Modified, "a.txt", false) } });

        var sync = await LoadAsync();

        sync.ChangedFiles[0].CanRevertChanges.Should().BeFalse();
        sync.ChangedFiles[0].RevertChangesCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a refresh that brings the same files keeps the rows, so a menu open on a row stays open, and that a
    /// refresh with other files makes them again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Refresh_With_The_Same_Files_Keeps_The_Rows()
    {
        var sync = await LoadAsync();
        var rows = sync.Rows.ToArray();

        await sync.RefreshAsync();

        sync.Rows.Should().Equal(rows, (left, right) => ReferenceEquals(left, right));

        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false, PartialChangeState.Partial), new ChangedFile(ChangeKind.Modified, "b.txt", false));
        await sync.RefreshAsync();

        sync.Rows.Should().NotContain(rows);
        sync.ChangedFiles[0].IsPartial.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that the row of a file is marked busy while a choice of its changes goes on, also when the list is made
    /// again meanwhile, that the actions that change the file wait, and that the mark goes when the work ends.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_In_Work_Is_Marked_And_Its_Actions_Wait()
    {
        var sync = await LoadAsync();
        var marked = new List<bool>();
        _dialogs.ShowFileDiffAsync(Arg.Any<FileDiffDialogViewModel>()).Returns(async _ =>
        {
            var row = sync.ChangedFiles[0];
            marked.Add(row.IsWorking);
            marked.Add(row.ChooseChangesCommand.CanExecute(null));
            marked.Add(sync.ChangedFiles[1].IsWorking);

            ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false, PartialChangeState.Partial), new ChangedFile(ChangeKind.Modified, "b.txt", false));
            await sync.RefreshAsync();
            marked.Add(sync.ChangedFiles[0].IsWorking);
            marked.Add(sync.ChangedFiles[0].ChooseAllChangesCommand.CanExecute(null));
            return false;
        });

        await sync.ChangedFiles[0].ChooseChangesCommand.ExecuteAsync(null);

        marked.Should().Equal(true, false, false, true, false);
        sync.ChangedFiles[0].IsWorking.Should().BeFalse();
        sync.ChangedFiles[0].ChooseAllChangesCommand.CanExecute(null).Should().BeTrue();
    }

    /// <summary>
    /// Creates the page and loads it.
    /// </summary>
    /// <returns>The page.</returns>
    private async Task<SyncViewModel> LoadAsync()
    {
        var sync = new SyncViewModel(TestTexts.Russian, _api, _dialogs);
        await sync.RefreshAsync();
        return sync;
    }

    /// <summary>
    /// Makes the service report changed files.
    /// </summary>
    /// <param name="files">The files.</param>
    private void ReturnChangedFiles(params ChangedFile[] files)
        => _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { ChangedFiles = files });

    /// <summary>
    /// Builds a diff of <c>a.txt</c> with two changes, both chosen.
    /// </summary>
    /// <returns>The diff.</returns>
    private static FileDiffResponse Diff()
        => new(
            "a.txt",
            DiffAvailability.Text,
            PartialChangeState.None,
            true,
            false,
            "base",
            "work",
            new[]
            {
                new DiffBlockDto(0, 1, 1, 1, 1, true, new[] { new DiffLineDto(DiffLineKind.Added, null, 2, "two") }),
                new DiffBlockDto(1, 5, 0, 5, 1, true, new[] { new DiffLineDto(DiffLineKind.Added, null, 6, "six") })
            });
}
