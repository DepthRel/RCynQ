using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of the conflict resolution flow of <see cref="SyncViewModel"/>.
/// </summary>
public sealed class SyncViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncViewModelTests"/> class.
    /// </summary>
    public SyncViewModelTests()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status());
    }

    /// <summary>
    /// Verifies that the chosen policy is sent to the service with the conflict files shown in the dialog.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Resolve_Sends_Chosen_Policy()
    {
        var patch = TestData.ConflictPatch();
        _api.GetPatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { patch });
        _api.ResolvePatchAsync(patch.Id, ConflictPolicy.ApplyIncomingOverLocal, Arg.Any<CancellationToken>())
            .Returns(new PatchApplyResult(patch.Id, PatchStatus.ConflictResolved, 1, Array.Empty<ConflictFile>(), null));
        ConflictDialogViewModel? shown = null;
        _dialogs.ShowConflictDialogAsync(Arg.Do<ConflictDialogViewModel>(dialog => shown = dialog)).Returns(ConflictPolicy.ApplyIncomingOverLocal);
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        var item = sync.History.Single();
        item.CanResolve.Should().BeTrue();
        await sync.ResolveCommand.ExecuteAsync(item);

        shown!.Files.Should().ContainSingle(file => file.Path == "src/app.cs");
        await _api.Received(1).ResolvePatchAsync(patch.Id, ConflictPolicy.ApplyIncomingOverLocal, Arg.Any<CancellationToken>());
        sync.InfoMessage.Should().Be("Конфликт разрешён.");
        sync.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that cancelling the dialog changes nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cancelled_Dialog_Does_Not_Resolve()
    {
        var patch = TestData.ConflictPatch();
        _api.GetPatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new[] { patch });
        _dialogs.ShowConflictDialogAsync(Arg.Any<ConflictDialogViewModel>()).Returns((ConflictPolicy?)null);
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        await sync.ResolveCommand.ExecuteAsync(sync.History.Single());

        await _api.DidNotReceiveWithAnyArgs().ResolvePatchAsync(default, default, default);
    }

    /// <summary>
    /// Verifies that service errors are shown instead of thrown.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Service_Error_Is_Shown()
    {
        _api.SendPatchAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns<SendPatchResult>(_ => throw new ServiceApiException(System.Net.HttpStatusCode.Conflict, "baseline missing"));
        var sync = new SyncViewModel(_api, _dialogs);

        await sync.SendCommand.ExecuteAsync(null);

        sync.ErrorMessage.Should().Be("baseline missing");
        sync.IsBusy.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the title of the list counts the files with the plural form of the language, and that files the neighbour
    /// already has stay in the list.
    /// </summary>
    /// <param name="count">The number of changed files.</param>
    /// <param name="expected">The expected text.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(1, "(1 файл)")]
    [InlineData(2, "(2 файла)")]
    [InlineData(5, "(5 файлов)")]
    [InlineData(10, "(10 файлов)")]
    [InlineData(11, "(11 файлов)")]
    [InlineData(21, "(21 файл)")]
    public async Task Changed_Files_Are_Counted_Next_To_The_Title(int count, string expected)
    {
        ReturnChangedFiles(Enumerable.Range(1, count).Select(index => new ChangedFile(ChangeKind.Modified, $"f{index}.txt", index % 2 == 0)).ToArray());
        var sync = new SyncViewModel(_api, _dialogs);

        await sync.RefreshAsync();

        sync.CountText.Should().Be(expected);
        sync.ChangedFiles.Should().HaveCount(count);
        sync.ChangedFiles.Count(file => file.AlreadySent).Should().Be(count / 2, "sent files are not hidden");
    }

    /// <summary>
    /// Verifies that nothing is counted when there are no changes.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Empty_List_Has_No_Count()
    {
        var sync = new SyncViewModel(_api, _dialogs);

        await sync.RefreshAsync();

        sync.CountText.Should().BeEmpty();
        sync.HasNoChangedFiles.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a service of an older version, which reports only the pending changes, still fills the list.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Older_Service_Fills_The_List_From_Pending_Changes()
    {
        var pending = new FileChange(ChangeKind.Added, "a.txt", new string('a', 64), 1, false);
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { PendingChanges = new[] { pending }, ChangedFiles = null });
        var sync = new SyncViewModel(_api, _dialogs);

        await sync.RefreshAsync();

        sync.ChangedFiles.Should().ContainSingle().Which.Should().Match<RCynQ.UI.ViewModels.Items.ChangedFileItemViewModel>(file => file.Path == "a.txt" && !file.AlreadySent);
    }

    /// <summary>
    /// Verifies that without check marks every file is sent, and the request names no files.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_Without_Check_Marks_Names_No_Files()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false), new ChangedFile(ChangeKind.Modified, "b.txt", true));
        _api.SendPatchAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns(new SendPatchResult(false, null, null, "Нечего отправлять."));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        await sync.SendCommand.ExecuteAsync(null);

        await _api.Received(1).SendPatchAsync(null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that with check marks only the checked files are sent, that sending needs at least one, and that the choice is
    /// forgotten after the send.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Send_With_Check_Marks_Names_The_Checked_Files()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false), new ChangedFile(ChangeKind.Added, "b.txt", false), new ChangedFile(ChangeKind.Deleted, "c.txt", true));
        _api.SendPatchAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns(new SendPatchResult(false, null, null, "Нечего отправлять."));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        sync.ToggleSelectionModeCommand.Execute(null);
        sync.ChangedFiles.Should().OnlyContain(file => file.IsSelectable);
        sync.CanSend.Should().BeFalse("nothing is checked");
        sync.SendCommand.CanExecute(null).Should().BeFalse();
        sync.CountText.Should().Be("(выбрано 0 из 3)");

        sync.ChangedFiles[1].IsSelected = true;
        sync.ChangedFiles[2].IsSelected = true;
        sync.SendCommand.CanExecute(null).Should().BeTrue();
        sync.CountText.Should().Be("(выбрано 2 из 3)");
        await sync.SendCommand.ExecuteAsync(null);

        await _api.Received(1).SendPatchAsync(Arg.Is<IReadOnlyList<string>?>(paths => paths != null && paths.SequenceEqual(new[] { "b.txt", "c.txt" })), Arg.Any<CancellationToken>());
        sync.ChangedFiles.Should().OnlyContain(file => !file.IsSelected);
        sync.IsSelectionMode.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that hiding the check marks forgets the choice, so that everything is sent.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Hiding_Check_Marks_Forgets_The_Choice()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false), new ChangedFile(ChangeKind.Modified, "b.txt", false));
        _api.SendPatchAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns(new SendPatchResult(false, null, null, "Нечего отправлять."));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.ToggleSelectionModeCommand.Execute(null);
        sync.SelectAllCommand.Execute(null);
        sync.SelectedCount.Should().Be(2);

        sync.ToggleSelectionModeCommand.Execute(null);
        await sync.SendCommand.ExecuteAsync(null);

        sync.ChangedFiles.Should().OnlyContain(file => !file.IsSelected && !file.IsSelectable);
        await _api.Received(1).SendPatchAsync(null, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a rescan keeps the files that are still changed checked.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Scan_Keeps_The_Checked_Files()
    {
        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "a.txt", false), new ChangedFile(ChangeKind.Modified, "b.txt", false));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.ToggleSelectionModeCommand.Execute(null);
        sync.ChangedFiles[1].IsSelected = true;

        ReturnChangedFiles(new ChangedFile(ChangeKind.Modified, "b.txt", false), new ChangedFile(ChangeKind.Modified, "c.txt", false));
        await sync.ScanCommand.ExecuteAsync(null);

        sync.ChangedFiles.Where(file => file.IsSelected).Select(file => file.Path).Should().Equal("b.txt");
        sync.ChangedFiles.Should().OnlyContain(file => file.IsSelectable);
    }

    /// <summary>
    /// Makes the service report changed files.
    /// </summary>
    /// <param name="files">The files.</param>
    private void ReturnChangedFiles(params ChangedFile[] files)
        => _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { ChangedFiles = files });

    /// <summary>
    /// Verifies that a Git sync base needs nothing from the user: no warning and no manual baseline, only the option to rebuild
    /// it from the commit.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Git_Sync_Base_Offers_Only_A_Rebuild()
    {
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        sync.NeedsSyncBase.Should().BeFalse();
        sync.CanCreateManualBaseline.Should().BeFalse();
        sync.CanRebuildBaseline.Should().BeTrue();
        sync.RebuildButtonText.Should().Be("Пересобрать базу синхронизации");
        sync.SyncBaseText.Should().Be("main @ eeeeeeee");
    }

    /// <summary>
    /// Verifies that rebuilding a Git sync base asks with the Git wording and reports it.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rebuilding_A_Git_Base_Uses_The_Git_Wording()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CreateBaselineAsync(Arg.Any<CancellationToken>()).Returns(new SnapshotInfo(Guid.NewGuid(), new NodeId("node-1"), DateTimeOffset.UtcNow, "hash", null));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        await sync.CreateBaselineCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.StartsWith("Пересобрать базу синхронизации из коммита Git", StringComparison.Ordinal)));
        sync.InfoMessage.Should().Be("База синхронизации пересобрана из коммита Git.");
    }

    /// <summary>
    /// Verifies that a node without a sync base shows why, and offers the manual baseline only when the service allows it.
    /// </summary>
    /// <param name="canCreateManual">Whether the working tree has no Git.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Missing_Sync_Base_Shows_The_Reason(bool canCreateManual)
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { SyncBase = SyncBaseStatus.NotReady("Выполните Pull.", canCreateManual) });
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        sync.NeedsSyncBase.Should().BeTrue();
        sync.SyncBaseProblem.Should().Be("Выполните Pull.");
        sync.CanCreateManualBaseline.Should().Be(canCreateManual);
        sync.CanRebuildBaseline.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that creating a manual baseline for the first time shows the plain confirmation and success text.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task First_Baseline_Uses_Create_Wording()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { SyncBase = SyncBaseStatus.NotReady("no git", true) });
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CreateBaselineAsync(Arg.Any<CancellationToken>()).Returns(new SnapshotInfo(Guid.NewGuid(), new NodeId("node-1"), DateTimeOffset.UtcNow, "hash", null));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.NeedsSyncBase.Should().BeTrue();

        await sync.CreateBaselineCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.StartsWith("Создать ручную базовую копию", StringComparison.Ordinal)));
        sync.InfoMessage.Should().Be("Базовая копия создана.");
    }

    /// <summary>
    /// Verifies that replacing an existing manual baseline shows the stronger recreate confirmation and success text.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Existing_Baseline_Uses_Recreate_Wording()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { SyncBase = new SyncBaseStatus(SyncBaseMode.Manual, null, null, null, true) });
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CreateBaselineAsync(Arg.Any<CancellationToken>()).Returns(new SnapshotInfo(Guid.NewGuid(), new NodeId("node-1"), DateTimeOffset.UtcNow, "hash", null));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.NeedsSyncBase.Should().BeFalse();
        sync.CanRebuildBaseline.Should().BeTrue();

        await sync.CreateBaselineCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), Arg.Is<string>(text => text.Contains("Заменить существующую базовую копию")));
        sync.InfoMessage.Should().Be("Базовая копия заменена.");
    }

    /// <summary>
    /// Verifies that cancelling the confirmation dialog leaves the baseline untouched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Cancelled_Baseline_Confirmation_Does_Not_Create()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(false);
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();

        await sync.CreateBaselineCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().CreateBaselineAsync(default);
    }
}
