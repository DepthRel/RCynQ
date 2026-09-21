using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
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
}
