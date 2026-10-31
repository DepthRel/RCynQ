using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of saving a patch on the Synchronization tab and of applying one on the Branches tab.
/// </summary>
public sealed class PatchFileViewModelTests : IDisposable
{
    /// <summary>The bytes of the patch of the tests.</summary>
    private static readonly byte[] _patch = "diff --git a/a.txt b/a.txt\n"u8.ToArray();

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>A temporary directory.</summary>
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "rcynq-ui-tests", Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchFileViewModelTests"/> class.
    /// </summary>
    public PatchFileViewModelTests()
    {
        Directory.CreateDirectory(_temp);
        _api.GetUnsentCommitsAsync(Arg.Any<CancellationToken>()).Returns(TestData.UnsentCommits());
        _api.GetPatchesAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<RCynQ.Core.Patches.PatchMetadata>());
        _api.BuildPatchFileAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns(_patch);
        _dialogs.PickPatchToSaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(PatchPath);
        _dialogs.PickPatchToOpenAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(PatchPath);

        // The dialog of the tests presses its Save button at once, with the choice the test left in it.
        _dialogs.ShowSavePatchDialogAsync(Arg.Any<SavePatchDialogViewModel>()).Returns(async call =>
        {
            var dialog = call.Arg<SavePatchDialogViewModel>();
            dialog.KeepChanges = KeepChanges;
            await dialog.SaveCommand.ExecuteAsync(null);
            return dialog.Confirmed;
        });
    }

    /// <summary>Gets the path of the patch file of a test.</summary>
    private string PatchPath => Path.Combine(_temp, "changes.patch");

    /// <summary>Gets or sets the choice about the changes the dialog of a test confirms.</summary>
    private bool KeepChanges { get; set; } = true;

    /// <summary>
    /// Verifies that the button is available exactly while there are changed files, with or without check marks.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Button_Follows_The_Changed_Files()
    {
        var sync = await CreateSyncAsync();
        sync.SavePatchCommand.CanExecute(null).Should().BeTrue();

        sync.ToggleSelectionModeCommand.Execute(null);
        sync.SavePatchCommand.CanExecute(null).Should().BeTrue("nothing checked means every file");

        Changed();
        await sync.RefreshAsync();

        sync.SavePatchCommand.CanExecute(null).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the dialog offers to keep the changes and that saving writes the file, under the suggested name.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saving_Writes_The_File_And_Keeps_The_Changes()
    {
        var sync = await CreateSyncAsync();

        new SavePatchDialogViewModel(_ => Task.FromResult(true)).KeepChanges.Should().BeTrue();
        await sync.SavePatchCommand.ExecuteAsync(null);

        (await File.ReadAllBytesAsync(PatchPath)).Should().Equal(_patch);
        await _dialogs.Received(1).PickPatchToSaveAsync(Arg.Any<string>(), "changes.patch", Arg.Any<string>());
        await _api.DidNotReceiveWithAnyArgs().ApplyPatchFileAsync(default!, default, default, default);
        sync.SuccessMessage.Should().Be(TestTexts.Russian.Format("SavePatch.Saved", PatchPath));
    }

    /// <summary>
    /// Verifies that no checked file saves every file and that checked files narrow the patch.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Patch_Holds_The_Checked_Files_Or_All()
    {
        var sync = await CreateSyncAsync();
        sync.ToggleSelectionModeCommand.Execute(null);

        await sync.SavePatchCommand.ExecuteAsync(null);
        await _api.Received(1).BuildPatchFileAsync(null, Arg.Any<CancellationToken>());

        sync.ChangedFiles.Single(file => file.Path == "b.txt").IsSelected = true;
        await sync.SavePatchCommand.ExecuteAsync(null);

        await _api.Received(1).BuildPatchFileAsync(Arg.Is<IReadOnlyList<string>?>(paths => paths != null && paths.Single() == "b.txt"), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that giving up on the file picker saves nothing and leaves the dialog open.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Giving_Up_On_The_Picker_Leaves_The_Dialog_Open()
    {
        _dialogs.PickPatchToSaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns((string?)null);
        SavePatchDialogViewModel? shown = null;
        _dialogs.ShowSavePatchDialogAsync(Arg.Do<SavePatchDialogViewModel>(dialog => shown = dialog)).Returns(async _ =>
        {
            await shown!.SaveCommand.ExecuteAsync(null);
            return shown.Confirmed;
        });
        var sync = await CreateSyncAsync();

        await sync.SavePatchCommand.ExecuteAsync(null);

        shown!.Confirmed.Should().BeFalse();
        await _api.DidNotReceiveWithAnyArgs().BuildPatchFileAsync(default, default);
        File.Exists(PatchPath).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that without the check mark the changes are taken out, after the file exists.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Without_The_Check_Mark_The_Changes_Leave_After_The_File_Is_Written()
    {
        KeepChanges = false;
        _api.ApplyPatchFileAsync(_patch, null, true, Arg.Any<CancellationToken>()).Returns(_ =>
        {
            File.Exists(PatchPath).Should().BeTrue("the changes leave the working branch only once they are safe in the file");
            return new PatchFileApplyResult(true, "taken out", "output");
        });
        var sync = await CreateSyncAsync();

        await sync.SavePatchCommand.ExecuteAsync(null);

        await _api.Received(1).ApplyPatchFileAsync(_patch, null, true, Arg.Any<CancellationToken>());
        sync.SuccessMessage.Should().Be(TestTexts.Russian.Format("SavePatch.SavedAndRemoved", PatchPath));
    }

    /// <summary>
    /// Verifies that changes that could not be taken out are reported with the reason, and the saved file stays.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Changes_That_Stayed_Are_Reported()
    {
        KeepChanges = false;
        _api.ApplyPatchFileAsync(_patch, null, true, Arg.Any<CancellationToken>()).Returns(new PatchFileApplyResult(false, "не подходит", "output"));
        var sync = await CreateSyncAsync();

        await sync.SavePatchCommand.ExecuteAsync(null);

        sync.ErrorMessage.Should().Be(TestTexts.Russian.Format("SavePatch.SavedNotRemoved", PatchPath, "не подходит"));
        File.Exists(PatchPath).Should().BeTrue();
    }

    /// <summary>
    /// Verifies that an empty patch writes no file: the files changed back in the meantime.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Empty_Patch_Writes_No_File()
    {
        _api.BuildPatchFileAsync(Arg.Any<IReadOnlyList<string>?>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<byte>());
        var sync = await CreateSyncAsync();

        await sync.SavePatchCommand.ExecuteAsync(null);

        File.Exists(PatchPath).Should().BeFalse();
        sync.InfoMessage.Should().Be(TestTexts.Russian.Get("SavePatch.Nothing"));
    }

    /// <summary>
    /// Verifies the way of a patch on the Branches tab: the file, the choice of its files, then the chosen ones go to the
    /// service, and the output of Git is shown.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Applying_Sends_The_Chosen_Files()
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _api.ReadPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new[]
        {
            new ChangedFile(ChangeKind.Modified, "a.txt", false),
            new ChangedFile(ChangeKind.Added, "b.txt", false)
        });
        _dialogs.ShowApplyPatchDialogAsync(Arg.Any<ApplyPatchDialogViewModel>()).Returns(call =>
        {
            var dialog = call.Arg<ApplyPatchDialogViewModel>();
            dialog.Files.Single(file => file.Path == "a.txt").IsSelected = false;
            return true;
        });
        _api.ApplyPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<IReadOnlyList<string>?>(), false, Arg.Any<CancellationToken>())
            .Returns(new PatchFileApplyResult(true, "применён", "Applied patch b.txt cleanly."));
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        var changed = false;
        branch.DataChanged += (_, _) => changed = true;

        await branch.ApplyPatchCommand.ExecuteAsync(null);

        await _api.Received(1).ApplyPatchFileAsync(
            Arg.Is<byte[]>(bytes => bytes.SequenceEqual(_patch)),
            Arg.Is<IReadOnlyList<string>?>(paths => paths != null && paths.Single() == "b.txt"),
            false,
            Arg.Any<CancellationToken>());
        branch.SuccessMessage.Should().Be("применён");
        branch.Output.Should().Contain("Applied patch b.txt");
        changed.Should().BeTrue("the Synchronization tab shows the new changes");
    }

    /// <summary>
    /// Verifies that a patch that does not fit shows the clear text of the service and the output of Git.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Patch_That_Does_Not_Fit_Is_Explained()
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _api.ReadPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new[] { new ChangedFile(ChangeKind.Modified, "a.txt", false) });
        _dialogs.ShowApplyPatchDialogAsync(Arg.Any<ApplyPatchDialogViewModel>()).Returns(true);
        _api.ApplyPatchFileAsync(Arg.Any<byte[]>(), null, false, Arg.Any<CancellationToken>())
            .Returns(new PatchFileApplyResult(false, "Patch не подходит", "error: a.txt: patch does not apply"));
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);

        await branch.ApplyPatchCommand.ExecuteAsync(null);

        branch.ErrorMessage.Should().Be("Patch не подходит");
        branch.Output.Should().Contain("patch does not apply");
    }

    /// <summary>
    /// Verifies that closing the picker or the choice of files applies nothing.
    /// </summary>
    /// <param name="pickerClosed">Whether the picker is closed; otherwise the choice of files.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Giving_Up_Applies_Nothing(bool pickerClosed)
    {
        await File.WriteAllBytesAsync(PatchPath, _patch);
        _dialogs.PickPatchToOpenAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(pickerClosed ? null : PatchPath);
        _api.ReadPatchFileAsync(Arg.Any<byte[]>(), Arg.Any<CancellationToken>()).Returns(new[] { new ChangedFile(ChangeKind.Modified, "a.txt", false) });
        _dialogs.ShowApplyPatchDialogAsync(Arg.Any<ApplyPatchDialogViewModel>()).Returns(false);
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);

        await branch.ApplyPatchCommand.ExecuteAsync(null);

        await _api.DidNotReceiveWithAnyArgs().ApplyPatchFileAsync(default!, default, default, default);
    }

    /// <summary>
    /// Verifies that a file that cannot be read shows an error instead of closing the application.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_File_That_Cannot_Be_Read_Is_Reported()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);

        await branch.ApplyPatchCommand.ExecuteAsync(null);

        branch.HasError.Should().BeTrue();
    }

    /// <summary>
    /// Deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        try
        {
            Directory.Delete(_temp, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Sets the state the service reports.
    /// </summary>
    /// <param name="paths">The changed files.</param>
    private void Changed(params string[] paths)
    {
        var status = TestData.Status() with { ChangedFiles = paths.Select(path => new ChangedFile(ChangeKind.Modified, path, false)).ToArray() };
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(status);
    }

    /// <summary>
    /// Creates the Synchronization tab with two changed files.
    /// </summary>
    /// <returns>The tab, loaded.</returns>
    private async Task<SyncViewModel> CreateSyncAsync()
    {
        Changed("a.txt", "b.txt");
        var sync = new SyncViewModel(TestTexts.Russian, _api, _dialogs);
        await sync.RefreshAsync();
        return sync;
    }
}
