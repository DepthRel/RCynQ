using FluentAssertions;
using NSubstitute;
using NSubstitute.ClearExtensions;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Tests.Support;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.ViewModels.Setup;

namespace RCynQ.UI.Tests.ViewModels;

/// <summary>
/// Tests of when the pages ask the service for data: once when first opened, in the background when an event concerns
/// the page on screen, and never merely because the user moved to another tab.
/// </summary>
public sealed class PageRefreshTests : IDisposable
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The event stream substitute.</summary>
    private readonly INodeEventStream _events = Substitute.For<INodeEventStream>();

    /// <summary>The service controller substitute.</summary>
    private readonly IServiceController _controller = Substitute.For<IServiceController>();

    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-refresh-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Initializes a new instance of the <see cref="PageRefreshTests"/> class.
    /// </summary>
    public PageRefreshTests()
    {
        var configPath = Path.Combine(_root, "syncbridge.json");
        File.WriteAllText(configPath, """{ "SyncBridge": { "NodeId": "node-1", "Role": "Workstation", "WorkdirPath": "/work" } }""");
        _controller.ConfigPath.Returns(configPath);
        _controller.EnsureRunningAsync(Arg.Any<CancellationToken>()).Returns(new ServiceStartResult(ServiceStartOutcome.AlreadyRunning, "running"));
        _events.IsConnected.Returns(true);
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        _api.GetStateAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status());
        _api.GetStateSummaryAsync(Arg.Any<CancellationToken>()).Returns(TestData.Status());
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkDto>());
        _api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(Array.Empty<LinkState>());
        _api.GetPatchesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<PatchMetadata>());
        _api.GetBranchAsync(Arg.Any<CancellationToken>()).Returns(new GitBranchInfo(true, "main", null, 0, 0));
        _api.GetLogsAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<LogEntryDto>());
    }

    /// <inheritdoc />
    public void Dispose() => Directory.Delete(_root, recursive: true);

    /// <summary>
    /// Verifies that the overview asks for the light status and never makes the service walk the working tree.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Overview_Uses_The_Status_Without_A_Scan_And_The_Configuration_It_Was_Given()
    {
        var main = Create();

        await main.InitializeAsync();

        main.CurrentPage.Should().BeSameAs(main.Overview);
        await _api.Received(1).GetStateSummaryAsync(Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetStateAsync(Arg.Any<CancellationToken>());
        await _api.Received(1).GetConfigAsync(Arg.Any<CancellationToken>());
        main.Overview.RoleText.Should().Be("Workstation");
    }

    /// <summary>
    /// Verifies that a page is loaded the first time it is opened and that coming back to it asks the service nothing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Moving_Between_Tabs_Asks_Nothing_Once_The_Pages_Are_Loaded()
    {
        var main = Create();
        await main.InitializeAsync();
        foreach (var key in new[] { TabKey.Sync, TabKey.Tunnel, TabKey.Branches, TabKey.Logs, TabKey.RoleAndNodes, TabKey.Overview })
        {
            main.Navigate(key);
        }

        _api.ClearReceivedCalls();
        foreach (var key in new[] { TabKey.Sync, TabKey.Tunnel, TabKey.Branches, TabKey.Logs, TabKey.RoleAndNodes, TabKey.Settings, TabKey.Diagnostics, TabKey.Overview, TabKey.Sync })
        {
            main.Navigate(key);
            main.CurrentPage!.IsBusy.Should().BeFalse();
        }

        _api.ReceivedCalls().Should().BeEmpty();
    }

    /// <summary>
    /// Verifies that a patch event reloads the synchronization page when it is on screen.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Event_Reloads_The_Page_On_Screen_In_The_Background()
    {
        var main = Create();
        await main.InitializeAsync();
        main.Navigate(TabKey.Sync);
        _api.ClearReceivedCalls();

        RaisePatch();

        await _api.Received(1).GetStateAsync(Arg.Any<CancellationToken>());
        await _api.DidNotReceive().GetStateSummaryAsync(Arg.Any<CancellationToken>());
        main.Sync.IsBusy.Should().BeFalse();
        main.Overview.IsCurrent.Should().BeFalse("the overview shows the same status and reloads when it is opened");
    }

    /// <summary>
    /// Verifies that a patch event costs nothing while the pages it concerns are not on screen, and that they reload
    /// when they are opened.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Patch_Event_For_Hidden_Pages_Is_Paid_For_When_They_Are_Opened()
    {
        var main = Create();
        await main.InitializeAsync();
        main.Navigate(TabKey.Sync);
        main.Navigate(TabKey.Logs);
        _api.ClearReceivedCalls();

        RaisePatch();
        RaisePatch();
        _api.ReceivedCalls().Should().BeEmpty();

        main.Navigate(TabKey.Sync);
        await _api.Received(1).GetStateAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an action on one page that changes node data makes the other pages reload when they are opened.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Action_On_One_Page_Outdates_The_Others()
    {
        _api.PullAsync(Arg.Any<CancellationToken>()).Returns(new GitOperationResult(true, "done", new GitBranchInfo(true, "main", "rcynq/main", 0, 0)));
        var main = Create();
        await main.InitializeAsync();
        main.Navigate(TabKey.Sync);
        main.Navigate(TabKey.Branches);
        _api.ClearReceivedCalls();

        await main.Branches.PullCommand.ExecuteAsync(null);

        main.Branches.IsCurrent.Should().BeTrue();
        main.Sync.IsCurrent.Should().BeFalse();
        main.Overview.IsCurrent.Should().BeFalse();
        main.Navigate(TabKey.Sync);
        await _api.Received(1).GetStateAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that after the event stream comes back the page on screen is reloaded and the others are outdated.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Reconnect_Reloads_What_Is_Shown()
    {
        var main = Create();
        await main.InitializeAsync();
        main.Navigate(TabKey.Sync);
        main.Navigate(TabKey.Overview);
        _api.ClearReceivedCalls();

        _events.ConnectionChanged += Raise.Event<EventHandler<bool>>(new object(), true);
        _api.ReceivedCalls().Should().BeEmpty("the stream was never down");

        _events.ConnectionChanged += Raise.Event<EventHandler<bool>>(new object(), false);
        _events.ConnectionChanged += Raise.Event<EventHandler<bool>>(new object(), true);

        await _api.Received(1).GetStateSummaryAsync(Arg.Any<CancellationToken>());
        main.Sync.IsCurrent.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that the Refresh command reloads the page on screen and shows the busy state while it does.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refresh_Command_Reloads_The_Page_On_Screen_As_A_User_Action()
    {
        var main = Create();
        await main.InitializeAsync();
        var answer = new TaskCompletionSource<NodeSyncStatus>();
        _api.ClearSubstitute(ClearOptions.ReceivedCalls);
        _api.GetStateSummaryAsync(Arg.Any<CancellationToken>()).Returns(answer.Task);

        var refresh = main.RefreshCommand.ExecuteAsync(null);

        main.Overview.IsBusy.Should().BeTrue();
        answer.SetResult(TestData.Status());
        await refresh;
        main.Overview.IsBusy.Should().BeFalse();
        await _api.Received(1).GetStateSummaryAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a background refresh never raises the busy state, that a failed one leaves the page outdated, and
    /// that its error goes away with the next successful one while the error of an action of the user stays.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Background_Refresh_Is_Quiet_And_Owns_Only_Its_Own_Error()
    {
        var page = new ProbePage(TestTexts.Russian);
        var busy = new List<bool>();
        page.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PageViewModelBase.IsBusy))
            {
                busy.Add(page.IsBusy);
            }
        };

        page.Failure = new HttpRequestException("down");
        await page.EnsureLoadedAsync();
        page.IsCurrent.Should().BeFalse();
        page.ErrorMessage.Should().Contain("down");

        page.Failure = null;
        await page.EnsureLoadedAsync();
        page.IsCurrent.Should().BeTrue();
        page.ErrorMessage.Should().BeNull();
        page.Loads.Should().Be(2);

        await page.EnsureLoadedAsync();
        page.Loads.Should().Be(2, "a page that is up to date is not loaded again");
        busy.Should().BeEmpty();

        await page.FailActionAsync("refused");
        await page.RefreshAsync();
        page.ErrorMessage.Should().Be("refused", "a background refresh does not hide what the last action reported");

        page.Invalidate();
        await page.ReloadAsync();
        busy.Should().Equal(true, false, true, false);
        page.IsCurrent.Should().BeTrue();
    }

    /// <summary>
    /// Raises a patch event of the service.
    /// </summary>
    private void RaisePatch()
        => _events.PatchChanged += Raise.Event<EventHandler<PatchEvent>>(
            new object(),
            new PatchEvent(Guid.NewGuid(), PatchDirection.Incoming, PatchStatus.Applied, 1, 0, null, DateTimeOffset.UtcNow));

    /// <summary>
    /// Creates the main view model over the substitutes.
    /// </summary>
    /// <returns>The view model.</returns>
    private MainViewModel Create()
    {
        var dialogs = Substitute.For<IDialogService>();
        return new MainViewModel(
            TestTexts.Russian,
            _api,
            _events,
            new ImmediateUiDispatcher(),
            new RoleAdapter(),
            _controller,
            new SetupViewModel(TestTexts.Russian, _controller.ConfigPath, dialogs),
            new OverviewViewModel(TestTexts.Russian, _api),
            new RoleAndNodesViewModel(TestTexts.Russian, _api, dialogs),
            new SyncViewModel(TestTexts.Russian, _api, dialogs),
            new TunnelViewModel(TestTexts.Russian, _api),
            new DiagnosticsViewModel(TestTexts.Russian, _api, dialogs),
            new BranchViewModel(TestTexts.Russian, _api),
            new LogsViewModel(TestTexts.Russian, _api, dialogs),
            new SettingsViewModel(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, dialogs));
    }

    /// <summary>
    /// A page whose loading the test controls.
    /// </summary>
    private sealed class ProbePage : PageViewModelBase
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ProbePage"/> class.
        /// </summary>
        /// <param name="localizer">The localizer of the texts.</param>
        public ProbePage(ILocalizer localizer)
            : base(localizer)
        {
        }

        /// <summary>Gets or sets the failure of the next load, or <see langword="null"/>.</summary>
        public Exception? Failure { get; set; }

        /// <summary>Gets the number of loads.</summary>
        public int Loads { get; private set; }

        /// <summary>
        /// Runs an action of the user that reports a problem of its own.
        /// </summary>
        /// <param name="message">The problem.</param>
        /// <returns>A task representing the action.</returns>
        public Task FailActionAsync(string message) => RunSafeAsync(() =>
        {
            ErrorMessage = message;
            return Task.CompletedTask;
        });

        /// <inheritdoc />
        protected override Task<bool> LoadAsync() => RunQuietAsync(() =>
        {
            Loads++;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        });
    }
}
