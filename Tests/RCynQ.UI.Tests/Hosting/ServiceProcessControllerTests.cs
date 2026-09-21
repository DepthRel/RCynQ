using FluentAssertions;
using NSubstitute;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Connection;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Tests.Support;

namespace RCynQ.UI.Tests.Hosting;

/// <summary>
/// Tests of <see cref="ServiceProcessController"/>.
/// </summary>
public sealed class ServiceProcessControllerTests : IDisposable
{
    /// <summary>The temporary directory.</summary>
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-controller-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>The fake launcher.</summary>
    private readonly FakeChildProcessLauncher _launcher = new();

    /// <summary>The configuration file.</summary>
    private readonly string _configPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceProcessControllerTests"/> class.
    /// </summary>
    public ServiceProcessControllerTests()
    {
        _configPath = Path.Combine(_root, "syncbridge.json");
        File.WriteAllText(_configPath, "{}");
    }

    /// <summary>
    /// Verifies that a service started elsewhere is used, not started again and not stopped.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Answering_Service_Is_Attached_And_Left_Running()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(true);
        using var controller = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));

        var result = await controller.EnsureRunningAsync();
        await controller.StopAsync();
        var restart = await controller.RestartAsync();

        result.Outcome.Should().Be(ServiceStartOutcome.AlreadyRunning);
        controller.Status.Should().Be(ServiceControllerStatus.Attached);
        controller.IsManaged.Should().BeFalse();
        restart.Outcome.Should().Be(ServiceStartOutcome.NotManaged);
        _launcher.Starts.Should().BeEmpty();
        await _api.DidNotReceive().ShutdownServiceAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that the service is started with the configuration, the parent process and the config directory.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Starts_Service_And_Waits_Until_It_Answers()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, false, true);
        using var controller = Create(new ServiceLaunchCommand("dotnet", new[] { "RCynQ.Service.dll" }));

        var result = await controller.EnsureRunningAsync();

        result.Outcome.Should().Be(ServiceStartOutcome.Started);
        controller.IsManaged.Should().BeTrue();
        controller.Status.Should().Be(ServiceControllerStatus.Running);
        var start = _launcher.Starts.Should().ContainSingle().Subject;
        start.FileName.Should().Be("dotnet");
        start.Arguments.Should().Equal("RCynQ.Service.dll", "--config", _configPath, "--parent-pid", "777", "--language", "ru");
        start.WorkingDirectory.Should().Be(_root);
    }

    /// <summary>
    /// Verifies that exit code 2 is reported as a configuration error with the service output.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Configuration_Error_Is_Reported()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false);
        _launcher.OnStart = process =>
        {
            process.ErrorOutput = "Invalid configuration: NodeId must not be blank.";
            process.Exit(2);
        };
        using var controller = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));

        var result = await controller.EnsureRunningAsync();

        result.IsConfigurationError.Should().BeTrue();
        result.Message.Should().Contain("NodeId must not be blank");
        controller.Status.Should().Be(ServiceControllerStatus.Failed);
        controller.IsManaged.Should().BeFalse();
    }

    /// <summary>
    /// Verifies a graceful stop: the shutdown request makes the process exit, so it is not killed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stop_Requests_Graceful_Shutdown()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, true);
        using var controller = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));
        await controller.EnsureRunningAsync();
        _api.ShutdownServiceAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _launcher.Processes.Single().Exit(0);
            return Task.CompletedTask;
        });

        await controller.StopAsync();

        _launcher.Processes.Single().Killed.Should().BeFalse();
        controller.Status.Should().Be(ServiceControllerStatus.Stopped);
        controller.IsManaged.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a service ignoring the shutdown request is killed after the stop timeout.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Stop_Kills_Service_That_Does_Not_Exit()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, true);
        _api.ShutdownServiceAsync(Arg.Any<CancellationToken>()).Returns(Task.FromException(new HttpRequestException("gone")));
        using var controller = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));
        await controller.EnsureRunningAsync();

        await controller.StopAsync();

        _launcher.Processes.Single().Killed.Should().BeTrue();
        controller.Status.Should().Be(ServiceControllerStatus.Stopped);
    }

    /// <summary>
    /// Verifies that a restart stops the managed service and starts a new process.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Restart_Starts_A_New_Process()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false, true, false, true);
        _api.ShutdownServiceAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            _launcher.Processes.Last().Exit(0);
            return Task.CompletedTask;
        });
        using var controller = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));
        await controller.EnsureRunningAsync();

        var result = await controller.RestartAsync();

        result.Outcome.Should().Be(ServiceStartOutcome.Started);
        _launcher.Processes.Should().HaveCount(2);
        _launcher.Processes[0].HasExited.Should().BeTrue();
        controller.IsManaged.Should().BeTrue();
    }

    /// <summary>
    /// Verifies the outcomes without a configuration file and without the service executable.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Config_Or_Executable_Is_Reported()
    {
        _api.IsHealthyAsync(Arg.Any<CancellationToken>()).Returns(false);
        using var withoutExecutable = Create(null);
        (await withoutExecutable.EnsureRunningAsync()).Outcome.Should().Be(ServiceStartOutcome.NotFound);

        File.Delete(_configPath);
        using var withoutConfig = Create(new ServiceLaunchCommand("svc", Array.Empty<string>()));
        var result = await withoutConfig.EnsureRunningAsync();
        result.Outcome.Should().Be(ServiceStartOutcome.NotConfigured);
        result.IsConfigurationError.Should().BeTrue();
        _launcher.Starts.Should().BeEmpty();
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
    /// Creates the controller under test with short timeouts.
    /// </summary>
    /// <param name="command">The launch command.</param>
    /// <returns>The controller.</returns>
    private ServiceProcessController Create(ServiceLaunchCommand? command)
        => new(
            new ServiceConnectionSettings(new Uri("http://127.0.0.1:1/"), string.Empty, ApiTokenProvider.ArgumentSource, _configPath),
            _api,
            _launcher,
            command,
            new ServiceControllerOptions
            {
                StartTimeout = TimeSpan.FromSeconds(5),
                StopTimeout = TimeSpan.FromMilliseconds(200),
                ShutdownRequestTimeout = TimeSpan.FromMilliseconds(200),
                PollInterval = TimeSpan.FromMilliseconds(10),
                ParentProcessId = 777
            });
}
