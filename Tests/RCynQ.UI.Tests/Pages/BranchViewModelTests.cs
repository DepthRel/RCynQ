using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Items;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Pages;

/// <summary>
/// Tests of <see cref="BranchViewModel"/>: the messages after Pull and Fetch, and the branch switcher.
/// </summary>
public sealed class BranchViewModelTests
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>
    /// Verifies that a Pull that succeeded shows a green message and a failed one a red message, and that each action
    /// hides the message of the previous one.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Pull_Shows_A_Green_Or_A_Red_Message()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.PullAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(true, "Fast-forward", null));

        await branch.PullCommand.ExecuteAsync(null);

        branch.SuccessMessage.Should().Be(TestTexts.Russian.Get("Branch.PullSucceeded"));
        branch.HasSuccess.Should().BeTrue();
        branch.HasError.Should().BeFalse();
        branch.Output.Should().Be("Fast-forward");

        _api.PullAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(false, "fatal: not possible to fast-forward", null));

        await branch.PullCommand.ExecuteAsync(null);

        branch.ErrorMessage.Should().Be(TestTexts.Russian.Get("Branch.PullFailed"));
        branch.HasSuccess.Should().BeFalse("the message of the previous Pull no longer applies");
    }

    /// <summary>
    /// Verifies that a Fetch that succeeded shows a green message and a failed one a red message.
    /// </summary>
    /// <param name="succeeded">Whether the Fetch succeeded.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fetch_Shows_A_Green_Or_A_Red_Message(bool succeeded)
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.FetchAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(succeeded, "output", null));

        await branch.FetchCommand.ExecuteAsync(null);

        branch.HasSuccess.Should().Be(succeeded);
        branch.HasError.Should().Be(!succeeded);
        (succeeded ? branch.SuccessMessage : branch.ErrorMessage)
            .Should().Be(TestTexts.Russian.Get(succeeded ? "Branch.FetchSucceeded" : "Branch.FetchFailed"));
    }

    /// <summary>
    /// Verifies that a Workstation lists every branch with its status, filters them by a part of the name, and warns
    /// when the Origin has another branch checked out.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task The_Switcher_Lists_And_Filters_Branches_And_Warns_About_The_Origin()
    {
        var branch = CreateWorkstationPage();

        await branch.EnsureLoadedAsync();

        branch.CanSwitch.Should().BeTrue();
        branch.FilteredBranches.Select(item => item.Name).Should().Equal("experiment", "feature/login", "main");
        branch.FilteredBranches.Single(item => item.Name == "main").IsCurrent.Should().BeTrue();
        branch.FilteredBranches.Single(item => item.Name == "feature/login").IsNotPulled.Should().BeTrue();
        branch.FilteredBranches.Single(item => item.Name == "experiment").StatusText.Should().Be(TestTexts.Russian.Get("Branch.Status.LocalOnly"));
        branch.HasOriginHeadWarning.Should().BeTrue();
        branch.OriginHeadWarning.Should().Be(TestTexts.Russian.Format("Branch.OriginHeadDiffers", "feature/login"));

        branch.BranchFilter = "LOG";

        branch.FilteredBranches.Select(item => item.Name).Should().Equal("feature/login");

        branch.BranchFilter = "nothing like it";

        branch.HasNoFilteredBranches.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a clean working tree is switched at once and the message of the service is shown in green.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task A_Clean_Switch_Needs_No_Dialog()
    {
        var branch = CreateWorkstationPage();
        await branch.EnsureLoadedAsync();
        _api.SwitchBranchAsync(Arg.Any<SwitchBranchRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SwitchBranchResult(BranchSwitchOutcome.Switched, "switched", Array.Empty<string>(), new GitBranchInfo(true, "feature/login", null, 0, 0)));

        await branch.SwitchToCommand.ExecuteAsync(Item(branch, "feature/login"));

        branch.SuccessMessage.Should().Be("switched");
        branch.CurrentBranch.Should().Be("feature/login");
        await _dialogs.DidNotReceive().ShowSwitchBranchDialogAsync(Arg.Any<SwitchBranchDialogViewModel>());
    }

    /// <summary>
    /// Verifies that changes in the way ask the user, that carrying them sends the second request, and that cancelling
    /// sends none.
    /// </summary>
    /// <param name="carry">Whether the user carries the changes over; otherwise the dialog is cancelled.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Changes_In_The_Way_Ask_The_User_First(bool carry)
    {
        var branch = CreateWorkstationPage();
        await branch.EnsureLoadedAsync();
        _api.SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.IfClean), Arg.Any<CancellationToken>())
            .Returns(new SwitchBranchResult(BranchSwitchOutcome.ChangesInTheWay, "changes", new[] { "a.txt" }, null));
        _api.SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.CarryChanges), Arg.Any<CancellationToken>())
            .Returns(new SwitchBranchResult(BranchSwitchOutcome.Switched, "carried", Array.Empty<string>(), null));
        _dialogs.ShowSwitchBranchDialogAsync(Arg.Any<SwitchBranchDialogViewModel>()).Returns(carry ? BranchSwitchMode.CarryChanges : null);

        await branch.SwitchToCommand.ExecuteAsync(Item(branch, "feature/login"));

        await _dialogs.Received(1).ShowSwitchBranchDialogAsync(Arg.Is<SwitchBranchDialogViewModel>(dialog => dialog.Files.SequenceEqual(new[] { "a.txt" })));
        await _api.Received(carry ? 1 : 0).SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.CarryChanges), Arg.Any<CancellationToken>());
        branch.SuccessMessage.Should().Be(carry ? "carried" : null);
        branch.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that saving the changes asks for the file from the open dialog, writes the patch there before the service
    /// takes them out, offers them back from that file, and says where they are when the user leaves them there.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saved_Changes_Are_Written_Before_The_Switch()
    {
        var file = TemporaryPatchPath();
        try
        {
            var branch = await CreateSavingPageAsync(file);

            await branch.SwitchToCommand.ExecuteAsync(Item(branch, "feature/login"));

            (await File.ReadAllBytesAsync(file)).Should().Equal(Patch);
            await _dialogs.Received(1).PickPatchToSaveAsync(Arg.Any<string>(), "feature-login-changes.patch", Arg.Any<string>());
            await _api.Received(1).SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.SavePatch && request.Patch!.SequenceEqual(Patch)), Arg.Any<CancellationToken>());
            await _dialogs.Received(1).ShowRestoreChangesDialogAsync(Arg.Is<RestoreChangesDialogViewModel>(dialog => dialog.PatchPath == file && dialog.Branch == "feature/login"));
            await _api.DidNotReceiveWithAnyArgs().ApplyPatchFileAsync(default!, default, default, default);
            branch.SuccessMessage.Should().Be($"switched\n{TestTexts.Russian.Format("BranchSwitch.SavedTo", file)}");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Verifies that the changes are brought back from the bytes saved to the file, without asking for the file again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Saved_Changes_Come_Back_After_The_Switch()
    {
        var file = TemporaryPatchPath();
        try
        {
            var branch = await CreateSavingPageAsync(file);
            _dialogs.ShowRestoreChangesDialogAsync(Arg.Any<RestoreChangesDialogViewModel>()).Returns(true);
            _api.ApplyPatchFileAsync(Arg.Any<byte[]>(), null, false, Arg.Any<CancellationToken>()).Returns(new PatchFileApplyResult(true, "applied", "git output"));

            await branch.SwitchToCommand.ExecuteAsync(Item(branch, "feature/login"));

            await _api.Received(1).ApplyPatchFileAsync(Arg.Is<byte[]>(bytes => bytes.SequenceEqual(Patch)), null, false, Arg.Any<CancellationToken>());
            await _dialogs.Received(1).PickPatchToSaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>());
            await _dialogs.DidNotReceiveWithAnyArgs().PickPatchToOpenAsync(default!, default!);
            branch.SuccessMessage.Should().Be($"switched\n{TestTexts.Russian.Format("BranchSwitch.Restored", file)}");
            branch.HasError.Should().BeFalse();
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Verifies that changes Git cannot bring back on the new branch stay in the file, and that the message says the
    /// branch was switched, why the changes did not fit, and where they are.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Changes_That_Do_Not_Fit_Stay_In_The_File()
    {
        var file = TemporaryPatchPath();
        try
        {
            var branch = await CreateSavingPageAsync(file);
            _dialogs.ShowRestoreChangesDialogAsync(Arg.Any<RestoreChangesDialogViewModel>()).Returns(true);
            _api.ApplyPatchFileAsync(Arg.Any<byte[]>(), null, false, Arg.Any<CancellationToken>()).Returns(new PatchFileApplyResult(false, "does not fit", "error: patch failed"));

            await branch.SwitchToCommand.ExecuteAsync(Item(branch, "feature/login"));

            File.Exists(file).Should().BeTrue();
            branch.CurrentBranch.Should().Be("feature/login");
            branch.HasSuccess.Should().BeFalse();
            branch.ErrorMessage.Should().Be($"switched\ndoes not fit\n{TestTexts.Russian.Format("BranchSwitch.RestoreFailed", file)}");
            branch.Output.Should().Be("error: patch failed");
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// Verifies that giving up on the file picker leaves the switch dialog open with no choice made, so nothing is switched.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Giving_Up_On_The_Picker_Leaves_The_Switch_Dialog_Open()
    {
        var closed = false;
        var dialog = new SwitchBranchDialogViewModel(TestTexts.Russian, "feature/login", new[] { "a.txt" }, () => Task.FromResult<string?>(null));
        dialog.CloseRequested += (_, _) => closed = true;

        await dialog.SavePatchCommand.ExecuteAsync(null);

        closed.Should().BeFalse();
        dialog.Result.Should().BeNull();
        dialog.PatchPath.Should().BeNull();
    }

    /// <summary>
    /// Verifies that a node that does not fetch from the Origin shows no switcher and does not ask for branches.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task An_Origin_Shows_No_Switcher()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        branch.Configure(RoleCapabilities.For(NodeRole.Origin));
        _api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", null, 0, 0));

        await branch.EnsureLoadedAsync();

        branch.CanSwitch.Should().BeFalse();
        branch.HasOriginHeadWarning.Should().BeFalse();
        await _api.DidNotReceive().GetSwitchableBranchesAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Creates the page of a Workstation on <c>main</c>, whose Origin has <c>feature/login</c> checked out.
    /// </summary>
    /// <returns>The page.</returns>
    private BranchViewModel CreateWorkstationPage()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        branch.Configure(RoleCapabilities.For(NodeRole.Workstation));
        _api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", "refs/remotes/rcynq/main", 0, 0));
        _api.GetSwitchableBranchesAsync(Arg.Any<CancellationToken>()).Returns(new GitSwitchBranchesResponse(
            "main",
            "feature/login",
            new[]
            {
                new GitSwitchableBranch("experiment", BranchSyncStatus.LocalOnly, 0, 0),
                new GitSwitchableBranch("feature/login", BranchSyncStatus.NotPulled, 0, 0),
                new GitSwitchableBranch("main", BranchSyncStatus.Pulled, 0, 0)
            }));
        return branch;
    }

    /// <summary>
    /// Creates the page of a Workstation whose working tree has changes in the way, where the dialog saves them to a file
    /// and the switch with the saved changes succeeds; the switch checks that the file was written first.
    /// </summary>
    /// <param name="file">The file the picker gives.</param>
    /// <returns>The loaded page.</returns>
    private async Task<BranchViewModel> CreateSavingPageAsync(string file)
    {
        var branch = CreateWorkstationPage();
        await branch.EnsureLoadedAsync();
        _api.SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.IfClean), Arg.Any<CancellationToken>())
            .Returns(new SwitchBranchResult(BranchSwitchOutcome.ChangesInTheWay, "changes", new[] { "a.txt" }, null));
        _api.SwitchBranchAsync(Arg.Is<SwitchBranchRequest>(request => request.Mode == BranchSwitchMode.SavePatch), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                File.Exists(file).Should().BeTrue("the changes leave the working tree only after the file is written");
                return new SwitchBranchResult(BranchSwitchOutcome.Switched, "switched", Array.Empty<string>(), new GitBranchInfo(true, "feature/login", null, 0, 0));
            });
        _api.BuildSwitchPatchAsync(Arg.Any<CancellationToken>()).Returns(Patch);
        _dialogs.PickPatchToSaveAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns(file);

        // The dialog of the tests presses its Save button at once, as the user would.
        _dialogs.ShowSwitchBranchDialogAsync(Arg.Any<SwitchBranchDialogViewModel>()).Returns(async call =>
        {
            var dialog = call.Arg<SwitchBranchDialogViewModel>();
            await dialog.SavePatchCommand.ExecuteAsync(null);
            return dialog.Result;
        });
        return branch;
    }

    /// <summary>
    /// Builds the path of a patch file of a test in the temporary folder.
    /// </summary>
    /// <returns>The path; the file does not exist yet.</returns>
    private static string TemporaryPatchPath() => Path.Combine(Path.GetTempPath(), $"rcynq-switch-{Guid.NewGuid():N}.patch");

    /// <summary>Gets the patch of the uncommitted changes in the tests that save them.</summary>
    private static byte[] Patch => "diff --git a/a.txt b/a.txt\n"u8.ToArray();

    /// <summary>
    /// Finds a row of the switcher.
    /// </summary>
    /// <param name="page">The page.</param>
    /// <param name="name">The branch name.</param>
    /// <returns>The row.</returns>
    private static SwitchBranchItemViewModel Item(BranchViewModel page, string name) => page.FilteredBranches.Single(item => item.Name == name);

    /// <summary>
    /// Verifies that hiding the messages hides the green one too.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Dismissing_Hides_The_Green_Message()
    {
        var branch = new BranchViewModel(TestTexts.Russian, _api, _dialogs);
        _api.FetchAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(true, "output", null));
        await branch.FetchCommand.ExecuteAsync(null);

        branch.DismissMessagesCommand.Execute(null);

        branch.HasSuccess.Should().BeFalse();
    }
}
