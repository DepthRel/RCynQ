using System.Net;
using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Services.Reset;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Pages;

namespace RCynQ.UI.Tests.Reset;

/// <summary>
/// Tests of the reset of the node from the settings: <see cref="NodeResetCoordinator"/>,
/// <see cref="ResetNodeDialogViewModel"/> and the danger zone of <see cref="SettingsViewModel"/>.
/// </summary>
public sealed class NodeResetTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-ui-reset-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The service controller substitute.</summary>
    private readonly IServiceController _controller = Substitute.For<IServiceController>();

    /// <summary>The eraser substitute.</summary>
    private readonly INodeDataEraser _eraser = Substitute.For<INodeDataEraser>();

    /// <summary>The dialog substitute.</summary>
    private readonly IDialogService _dialogs = Substitute.For<IDialogService>();

    /// <summary>A plan of a reset.</summary>
    private readonly NodeResetPlan _plan = new("node-1", "/cfg/syncbridge.json", "/data", true, new[] { "/data/rcynq.db" });

    /// <summary>
    /// Verifies the order of a reset: the lines in authorized_keys go while the service runs, then the service stops, and
    /// only then are its files removed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reset_Revokes_Then_Stops_Then_Erases()
    {
        _api.RevokePeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new AccessRevocationResult("/home/u/.ssh/authorized_keys", 2, false));
        _eraser.Erase(_plan).Returns(new NodeResetReport(5, Array.Empty<string>()));

        var outcome = await Coordinator().ResetAsync(_plan);

        Received.InOrder(() =>
        {
            _api.RevokePeerAccessAsync(Arg.Any<CancellationToken>());
            _controller.StopAsync(Arg.Any<CancellationToken>());
            _eraser.Erase(_plan);
        });
        outcome.Report.IsComplete.Should().BeTrue();
        outcome.AccessText.Should().Be(TestTexts.Russian.Format("Reset.AccessRemoved", 2, "/home/u/.ssh/authorized_keys"));
    }

    /// <summary>
    /// Verifies that lines left for an administrator are reported, that no lines say nothing, and that a service that
    /// could not answer does not stop the reset.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Access_Outcome_Is_Worded_And_A_Failure_Does_Not_Stop_The_Reset()
    {
        _eraser.Erase(_plan).Returns(new NodeResetReport(5, Array.Empty<string>()));

        _api.RevokePeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new AccessRevocationResult("C:/ProgramData/ssh/administrators_authorized_keys", 0, true));
        (await Coordinator().ResetAsync(_plan)).AccessText.Should().Be(TestTexts.Russian.Format("Reset.AccessNeedsAdmin", "C:/ProgramData/ssh/administrators_authorized_keys"));

        _api.RevokePeerAccessAsync(Arg.Any<CancellationToken>()).Returns(new AccessRevocationResult("/home/u/.ssh/authorized_keys", 0, false));
        (await Coordinator().ResetAsync(_plan)).AccessText.Should().BeNull();

        _api.RevokePeerAccessAsync(Arg.Any<CancellationToken>()).Returns<AccessRevocationResult>(_ => throw new ServiceApiException(HttpStatusCode.BadRequest, "the file is locked"));
        var failed = await Coordinator().ResetAsync(_plan);
        failed.AccessText.Should().Be(TestTexts.Russian.Format("Reset.AccessFailed", "the file is locked"));
        _eraser.Received(3).Erase(_plan);
    }

    /// <summary>
    /// Verifies that only the service this application started can be reset, and that the plan comes from the
    /// configuration file, or is missing when the file cannot be read.
    /// </summary>
    [Fact]
    public void Reset_Needs_A_Managed_Service_And_A_Readable_Configuration()
    {
        var config = Path.Combine(_root, "syncbridge.json");
        _controller.ConfigPath.Returns(config);
        _controller.IsManaged.Returns(false);
        Coordinator().CanReset.Should().BeFalse();
        _controller.IsManaged.Returns(true);
        Coordinator().CanReset.Should().BeTrue();

        Coordinator().Plan().Should().BeNull("there is no configuration file");

        File.WriteAllText(config, "{ \"SyncBridge\": { \"NodeId\": \"node-1\" } }");
        _eraser.Plan(Arg.Any<SyncBridgeOptions>(), config, null).Returns(_plan);
        Coordinator().Plan().Should().BeSameAs(_plan);
        _eraser.Received(1).Plan(Arg.Is<SyncBridgeOptions>(options => options.NodeId == "node-1"), config, null);
    }

    /// <summary>
    /// Verifies that the reset can start only once the exact identifier of the node is typed, and that the dialog names
    /// what goes.
    /// </summary>
    [Fact]
    public void Dialog_Wakes_Up_Only_For_The_Identifier()
    {
        var dialog = new ResetNodeDialogViewModel(TestTexts.Russian, _plan);

        dialog.ConfigText.Should().Be(TestTexts.Russian.Get("Reset.Config"));
        dialog.ConfigPath.Should().Be("/cfg/syncbridge.json");
        dialog.DataText.Should().Be(TestTexts.Russian.Get("Reset.Data"));
        dialog.DataPath.Should().Be("/data");
        dialog.PromptText.Should().Contain("node-1");
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();

        dialog.Typed = "node";
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse();
        dialog.Typed = "NODE-1";
        dialog.ConfirmCommand.CanExecute(null).Should().BeFalse("the identifier is typed exactly");
        dialog.Typed = " node-1 ";
        dialog.ConfirmCommand.CanExecute(null).Should().BeTrue();

        dialog.ConfirmCommand.Execute(null);
        dialog.Confirmed.Should().BeTrue();

        new ResetNodeDialogViewModel(TestTexts.Russian, _plan with { HasData = false }).DataText.Should().Be(TestTexts.Russian.Get("Reset.NoData"));
    }

    /// <summary>
    /// Verifies that the danger zone resets the node only after the dialog was confirmed, and tells the window when it did.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Danger_Zone_Resets_Only_After_Confirmation()
    {
        var reset = Substitute.For<INodeResetCoordinator>();
        reset.CanReset.Returns(true);
        reset.Plan().Returns(_plan);
        var outcome = new NodeResetOutcome(new NodeResetReport(5, Array.Empty<string>()), null);
        reset.ResetAsync(_plan, Arg.Any<CancellationToken>()).Returns(outcome);
        var settings = Settings(reset);
        var completed = new List<NodeResetOutcome>();
        settings.ResetCompleted += (_, value) => completed.Add(value);

        settings.HasReset.Should().BeTrue();
        settings.CanReset.Should().BeTrue();

        _dialogs.ShowResetNodeDialogAsync(Arg.Any<ResetNodeDialogViewModel>()).Returns(false);
        await settings.ResetNodeCommand.ExecuteAsync(null);
        await reset.DidNotReceive().ResetAsync(Arg.Any<NodeResetPlan>(), Arg.Any<CancellationToken>());
        completed.Should().BeEmpty();

        _dialogs.ShowResetNodeDialogAsync(Arg.Any<ResetNodeDialogViewModel>()).Returns(true);
        await settings.ResetNodeCommand.ExecuteAsync(null);
        await reset.Received(1).ResetAsync(_plan, Arg.Any<CancellationToken>());
        completed.Should().Equal(outcome);
        settings.HasError.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the files that could not be removed are named, that a service started elsewhere is not reset, and
    /// that a configuration that cannot be read is reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Danger_Zone_Reports_What_Stops_The_Reset()
    {
        var reset = Substitute.For<INodeResetCoordinator>();
        reset.CanReset.Returns(true);
        reset.Plan().Returns(_plan);
        reset.ResetAsync(_plan, Arg.Any<CancellationToken>()).Returns(new NodeResetOutcome(new NodeResetReport(3, new[] { "/data/rcynq.db" }), null));
        _dialogs.ShowResetNodeDialogAsync(Arg.Any<ResetNodeDialogViewModel>()).Returns(true);
        var settings = Settings(reset);

        await settings.ResetNodeCommand.ExecuteAsync(null);
        settings.ErrorMessage.Should().Be(TestTexts.Russian.Format("Reset.Incomplete", "/data/rcynq.db"));

        reset.Plan().Returns((NodeResetPlan?)null);
        await settings.ResetNodeCommand.ExecuteAsync(null);
        settings.ErrorMessage.Should().Be(TestTexts.Russian.Get("Reset.NoConfig"));

        reset.CanReset.Returns(false);
        reset.ClearReceivedCalls();
        await settings.ResetNodeCommand.ExecuteAsync(null);
        reset.DidNotReceive().Plan();
        Settings(null).HasReset.Should().BeFalse();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates the coordinator under test with no portable installation.
    /// </summary>
    /// <returns>The coordinator.</returns>
    private NodeResetCoordinator Coordinator() => new(TestTexts.Russian, _api, _controller, _eraser, () => null);

    /// <summary>
    /// Creates the settings page.
    /// </summary>
    /// <param name="reset">Resets the node, or <see langword="null"/> when the reset is not offered.</param>
    /// <returns>The page.</returns>
    private SettingsViewModel Settings(INodeResetCoordinator? reset)
        => new(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, _dialogs, reset: reset);
}
