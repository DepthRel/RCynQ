using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Enums;
using RCynQ.Ssh.Enums;
using RCynQ.Ssh.Server;

namespace RCynQ.Ssh.Tests;

/// <summary>
/// Tests of <see cref="ServerSetupService"/>: how the end of a command with administrator rights is worded on every
/// system.
/// </summary>
public sealed class ServerSetupServiceTests
{
    /// <summary>The description of the computer.</summary>
    private readonly IHostSystemSource _system = Substitute.For<IHostSystemSource>();

    /// <summary>The runner of commands with administrator rights.</summary>
    private readonly IElevatedProcessLauncher _launcher = Substitute.For<IElevatedProcessLauncher>();

    /// <summary>
    /// Verifies the outcome of every way a run can end, per system.
    /// </summary>
    /// <param name="platform">The system.</param>
    /// <param name="exitCode">The exit code of the command.</param>
    /// <param name="error">What the command wrote to its error output.</param>
    /// <param name="declined">Whether the UAC prompt was dismissed.</param>
    /// <param name="expected">The expected outcome.</param>
    /// <returns>A task representing the test.</returns>
    [Theory]
    [InlineData(HostPlatform.Windows, 0, "", false, ServerSetupOutcome.Completed)]
    [InlineData(HostPlatform.Windows, 1223, "", true, ServerSetupOutcome.Cancelled)]
    [InlineData(HostPlatform.Windows, 2, "", false, ServerSetupOutcome.Failed)]
    [InlineData(HostPlatform.Linux, 0, "", false, ServerSetupOutcome.Completed)]
    [InlineData(HostPlatform.Linux, 126, "", false, ServerSetupOutcome.Cancelled)]
    [InlineData(HostPlatform.Linux, 100, "E: Unable to locate package", false, ServerSetupOutcome.Failed)]
    [InlineData(HostPlatform.MacOs, 1, "execution error: User canceled. (-128)", false, ServerSetupOutcome.Cancelled)]
    [InlineData(HostPlatform.MacOs, 1, "execution error: requires Full Disk Access", false, ServerSetupOutcome.Failed)]
    public async Task Install_Words_How_The_Run_Ended(HostPlatform platform, int exitCode, string error, bool declined, ServerSetupOutcome expected)
    {
        _system.Describe().Returns(new HostSystem(platform, new[] { "debian" }, new HashSet<string> { "apt-get", "systemctl", "pkexec" }));
        _launcher.RunAsync(Arg.Any<ElevatedCommand>(), Arg.Any<CancellationToken>()).Returns(new ElevatedProcessExit(exitCode, error, declined));

        var result = await Create().InstallSshServerAsync();

        result.Outcome.Should().Be(expected);
        result.IsReady.Should().Be(expected == ServerSetupOutcome.Completed);
        result.ManualCommands.Should().Match(commands => expected == ServerSetupOutcome.Failed ? !string.IsNullOrEmpty(commands) : commands == null);
    }

    /// <summary>
    /// Verifies that Windows without access to Windows Update gets the way through the installer package.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Windows_Without_Windows_Update_Is_Told_About_The_Installer_Package()
    {
        _system.Describe().Returns(new HostSystem(HostPlatform.Windows, Array.Empty<string>(), new HashSet<string>()));
        _launcher.RunAsync(Arg.Any<ElevatedCommand>(), Arg.Any<CancellationToken>()).Returns(new ElevatedProcessExit(ServerSetupPlanner.WINDOWS_UPDATE_MISSING, string.Empty));

        var result = await Create().InstallSshServerAsync();

        result.Outcome.Should().Be(ServerSetupOutcome.Failed);
        result.Message.Should().Be(TestTexts.English.Get("ServerSetup.WindowsUpdateMissing"));
    }

    /// <summary>
    /// Verifies that nothing runs when the system cannot be changed automatically or needs no change, and that a command
    /// that cannot start still leaves the commands by hand.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Nothing_Runs_Without_A_Command()
    {
        _system.Describe().Returns(new HostSystem(HostPlatform.Linux, new[] { "debian" }, new HashSet<string> { "apt-get", "systemctl" }));
        var unsupported = await Create().InstallSshServerAsync();
        var notNeeded = await Create().OpenEndpointPortAsync(18444);

        unsupported.Outcome.Should().Be(ServerSetupOutcome.Unsupported);
        unsupported.Message.Should().Be(TestTexts.English.Get("ServerSetup.NoPkexec"));
        unsupported.ManualCommands.Should().Contain("sudo sh -c");
        notNeeded.Outcome.Should().Be(ServerSetupOutcome.NotNeeded);
        notNeeded.IsReady.Should().BeTrue();
        await _launcher.DidNotReceiveWithAnyArgs().RunAsync(default!, default);

        _system.Describe().Returns(new HostSystem(HostPlatform.Windows, Array.Empty<string>(), new HashSet<string>()));
        _launcher.RunAsync(Arg.Any<ElevatedCommand>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("no shell"));
        var notStarted = await Create().OpenEndpointPortAsync(18444);

        notStarted.Outcome.Should().Be(ServerSetupOutcome.Failed);
        notStarted.Message.Should().Contain("no shell");
        notStarted.ManualCommands.Should().Contain("-LocalPort 18444");
    }

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <returns>The service.</returns>
    private ServerSetupService Create() => new(TestTexts.English, _system, _launcher);
}
