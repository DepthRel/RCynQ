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
        _api.SendPatchAsync(Arg.Any<CancellationToken>()).Returns<SendPatchResult>(_ => throw new ServiceApiException(System.Net.HttpStatusCode.Conflict, "baseline missing"));
        var sync = new SyncViewModel(_api, _dialogs);

        await sync.SendCommand.ExecuteAsync(null);

        sync.ErrorMessage.Should().Be("baseline missing");
        sync.IsBusy.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that creating a baseline for the first time shows the plain confirmation and success text.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task First_Baseline_Uses_Create_Wording()
    {
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status() with { BaselineInitialized = false });
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CreateBaselineAsync(Arg.Any<CancellationToken>()).Returns(new SnapshotInfo(Guid.NewGuid(), new NodeId("node-1"), DateTimeOffset.UtcNow, "hash", null));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.NeedsBaseline.Should().BeTrue();

        await sync.CreateBaselineCommand.ExecuteAsync(null);

        await _dialogs.Received(1).ConfirmAsync(Arg.Any<string>(), "Создать базовую копию из текущей рабочей копии? Делайте это на Source и Dest, когда их рабочие копии совпадают.");
        sync.InfoMessage.Should().Be("Базовая копия создана.");
    }

    /// <summary>
    /// Verifies that replacing an existing baseline shows the stronger recreate confirmation and success text.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Existing_Baseline_Uses_Recreate_Wording()
    {
        _dialogs.ConfirmAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(true);
        _api.CreateBaselineAsync(Arg.Any<CancellationToken>()).Returns(new SnapshotInfo(Guid.NewGuid(), new NodeId("node-1"), DateTimeOffset.UtcNow, "hash", null));
        var sync = new SyncViewModel(_api, _dialogs);
        await sync.RefreshAsync();
        sync.NeedsBaseline.Should().BeFalse();

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
