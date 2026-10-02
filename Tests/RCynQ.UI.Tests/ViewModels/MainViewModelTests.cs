using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
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
/// Tests of <see cref="MainViewModel"/>: service start, setup, role adaptation and routing of live hub events.
/// </summary>
public sealed class MainViewModelTests : IDisposable
{
    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The event stream substitute.</summary>
    private readonly INodeEventStream _events = Substitute.For<INodeEventStream>();

    /// <summary>The service controller substitute.</summary>
    private readonly IServiceController _controller = Substitute.For<IServiceController>();

    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-main-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The configuration file.</summary>
    private readonly string _configPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModelTests"/> class.
    /// </summary>
    public MainViewModelTests()
    {
        _configPath = Path.Combine(_root, "syncbridge.json");
        File.WriteAllText(_configPath, """{ "SyncBridge": { "NodeId": "node-1", "Role": "Relay" } }""");
        _controller.ConfigPath.Returns(_configPath);
        _controller.EnsureRunningAsync(Arg.Any<CancellationToken>()).Returns(new ServiceStartResult(ServiceStartOutcome.AlreadyRunning, "running"));
    }

    /// <summary>
    /// Verifies that a Relay hides synchronization and branches and selects the overview.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Relay_Hides_Sync_And_Branches()
    {
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Relay));
        var main = Create();

        await main.InitializeAsync();

        main.IsSetupVisible.Should().BeFalse();
        main.Tabs.Single(tab => tab.Key == TabKey.Sync).IsAvailable.Should().BeFalse();
        main.Tabs.Single(tab => tab.Key == TabKey.Branches).IsAvailable.Should().BeFalse();
        main.SelectedTab!.Key.Should().Be(TabKey.Overview);
        main.CurrentPage.Should().BeSameAs(main.Overview);
        await _controller.Received(1).EnsureRunningAsync(Arg.Any<CancellationToken>());
        await _events.Received(1).ConnectAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that without a configuration file the setup is shown and no service is started.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Config_Shows_Setup()
    {
        File.Delete(_configPath);
        var main = Create();

        await main.InitializeAsync();

        main.IsSetupVisible.Should().BeTrue();
        main.Setup.NodeId.Should().NotBeNullOrEmpty();
        await _controller.DidNotReceive().EnsureRunningAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a configuration rejected by the service opens the setup with the service message.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Rejected_Config_Shows_Setup_With_Error()
    {
        _controller.EnsureRunningAsync(Arg.Any<CancellationToken>()).Returns(new ServiceStartResult(ServiceStartOutcome.Failed, "Invalid configuration: Chain[0] ...", 2));
        var main = Create();

        await main.InitializeAsync();

        main.IsSetupVisible.Should().BeTrue();
        main.Setup.Error.Should().Contain("Chain[0]");
        main.Setup.NodeId.Should().Be("node-1");
        main.Setup.SelectedRole.Should().Be(NodeRole.Relay);
    }

    /// <summary>
    /// Verifies that completing the setup starts the service and shows the tabs.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Completed_Setup_Starts_Service()
    {
        File.Delete(_configPath);
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Relay));
        var main = Create();
        await main.InitializeAsync();

        main.Setup.SelectedRole = NodeRole.Relay;
        await main.Setup.SaveCommand.ExecuteAsync(null);

        await Eventually.WaitUntilAsync(() => !main.IsSetupVisible && main.SelectedTab is not null);
        File.Exists(_configPath).Should().BeTrue();
        await _controller.Received(1).EnsureRunningAsync(Arg.Any<CancellationToken>());
        main.Tabs.Single(tab => tab.Key == TabKey.Sync).IsAvailable.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that saving a new role restarts the service and adapts the tabs to the new role.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Role_Change_Restarts_Service()
    {
        var role = NodeRole.Workstation;
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(_ => TestData.Config(role));
        _api.SetRoleAsync(Arg.Any<SetRoleRequest>(), Arg.Any<CancellationToken>()).Returns(new RoleChangeResult(true, true, "saved"));
        _controller.RestartAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            role = NodeRole.Relay;
            return new ServiceStartResult(ServiceStartOutcome.Started, "restarted");
        });
        var main = Create();
        await main.InitializeAsync();
        main.Tabs.Single(tab => tab.Key == TabKey.Sync).IsAvailable.Should().BeTrue();

        main.RoleAndNodes.SelectedRole = NodeRole.Relay;
        await main.RoleAndNodes.ApplyRoleCommand.ExecuteAsync(null);

        await Eventually.WaitUntilAsync(() => !main.Tabs.Single(tab => tab.Key == TabKey.Sync).IsAvailable);
        await _controller.Received(1).RestartAsync(Arg.Any<CancellationToken>());
        await Eventually.WaitUntilAsync(() => main.RoleAndNodes.InfoMessage == "Роль применена: сервис перезапущен.");
    }

    /// <summary>
    /// Verifies that a failed link event updates the tunnel page and marks the tab.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Link_Event_Updates_Pages_And_Marks_Tunnel()
    {
        var link = TestData.Link("relay", "relay-1", NodeRole.Relay, "203.0.113.4");
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        _api.GetLinksAsync(Arg.Any<CancellationToken>()).Returns(new[] { link });
        _api.GetLinkStatusAsync(Arg.Any<CancellationToken>()).Returns(new[] { TestData.State(link, LinkStatus.Ready) });
        var main = Create();
        await main.InitializeAsync();
        await main.Tunnel.RefreshAsync();

        _events.LinkStateChanged += Raise.Event<EventHandler<LinkStateChangedEvent>>(new object(), new LinkStateChangedEvent(TestData.State(link, LinkStatus.Failed)));

        main.Tunnel.Links.Single().IsOffline.Should().BeTrue();
        main.Tabs.Single(tab => tab.Key == TabKey.Tunnel).HasAttention.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that failed diagnostics are routed to the diagnostics page and the overview problems.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Diagnostics_Event_Is_Routed()
    {
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Origin));
        var main = Create();
        await main.InitializeAsync();

        var run = new DiagnosticsEvent(NodeRole.Origin, new[] { new DiagnosticResult("GitInstalled", false, "git not found", "install git") }, DateTimeOffset.UtcNow);
        _events.DiagnosticsCompleted += Raise.Event<EventHandler<DiagnosticsEvent>>(new object(), run);

        main.Diagnostics.Results.Should().ContainSingle().Which.CanFix.Should().BeFalse();
        main.Overview.Problems.Should().Contain(problem => problem.Target == TabKey.Diagnostics);
        main.Tabs.Single(tab => tab.Key == TabKey.Diagnostics).HasAttention.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that selecting a tab clears its attention mark.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Selecting_Tab_Clears_Attention()
    {
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(TestData.Config(NodeRole.Workstation));
        var main = Create();
        await main.InitializeAsync();
        var diagnostics = main.Tabs.Single(tab => tab.Key == TabKey.Diagnostics);
        diagnostics.HasAttention = true;

        main.SelectTabCommand.Execute(diagnostics);

        main.SelectedTab.Should().BeSameAs(diagnostics);
        diagnostics.IsSelected.Should().BeTrue();
        diagnostics.HasAttention.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that refreshing retries initialization when the service was down at startup.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Refresh_Retries_Initialization_After_Startup_Failure()
    {
        _api.GetConfigAsync(Arg.Any<CancellationToken>()).Returns(
            _ => Task.FromException<NodeConfigurationDto>(new HttpRequestException("service down")),
            _ => Task.FromResult(TestData.Config(NodeRole.Relay)));
        var main = Create();

        await main.InitializeAsync();
        main.ConnectionText.Should().Contain("service down");
        await _events.DidNotReceive().ConnectAsync(Arg.Any<CancellationToken>());

        await main.RefreshCommand.ExecuteAsync(null);

        main.NodeTitle.Should().Contain("Relay");
        main.Tabs.Single(tab => tab.Key == TabKey.Sync).IsAvailable.Should().BeFalse();
        await _events.Received(1).ConnectAsync(Arg.Any<CancellationToken>());
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
    /// Creates the view model under test.
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
            new SetupViewModel(TestTexts.Russian, _configPath, dialogs),
            new OverviewViewModel(TestTexts.Russian, _api),
            new RoleAndNodesViewModel(TestTexts.Russian, _api, dialogs),
            new SyncViewModel(TestTexts.Russian, _api, dialogs),
            new TunnelViewModel(TestTexts.Russian, _api),
            new DiagnosticsViewModel(TestTexts.Russian, _api, dialogs),
            new BranchViewModel(TestTexts.Russian, _api),
            new LogsViewModel(TestTexts.Russian, _api, dialogs),
            new SettingsViewModel(TestTexts.Russian, new LanguageViewModel(TestTexts.Russian, new FakeLanguageService()), _api, dialogs));
    }
}
