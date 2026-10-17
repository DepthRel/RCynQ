using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Ssh.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// Installs the SSH server or opens the TLS port with the commands of <see cref="ServerSetupPlanner"/>, asking the user
/// for administrator rights, and words the outcome. Every failure carries the commands to run by hand.
/// </summary>
public sealed class ServerSetupService : IServerSetup
{
    /// <summary>The exit code of <c>pkexec</c> when the user dismissed its prompt or was not allowed.</summary>
    private const int PKEXEC_DISMISSED = 126;

    /// <summary>The AppleScript error of a dismissed administrator prompt.</summary>
    private const string MAC_USER_CANCELED = "(-128)";

    /// <summary>The description of the computer.</summary>
    private readonly IHostSystemSource _system;

    /// <summary>The runner of commands with administrator rights.</summary>
    private readonly IElevatedProcessLauncher _launcher;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerSetupService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="system">The description of the computer.</param>
    /// <param name="launcher">The runner of commands with administrator rights.</param>
    public ServerSetupService(ILocalizer localizer, IHostSystemSource system, IElevatedProcessLauncher launcher)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _system = system ?? throw new ArgumentNullException(nameof(system));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
    }

    /// <inheritdoc />
    public Task<ServerSetupResult> InstallSshServerAsync(CancellationToken cancellationToken = default)
        => RunAsync(ServerSetupPlanner.PlanSshServer(_system.Describe()), _localizer.Get("ServerSetup.SshReady"), cancellationToken);

    /// <inheritdoc />
    public Task<ServerSetupResult> OpenEndpointPortAsync(int port, CancellationToken cancellationToken = default)
    {
        var plan = ServerSetupPlanner.PlanOpenPort(_system.Describe(), port);

        return plan is null
            ? Task.FromResult(new ServerSetupResult(ServerSetupOutcome.NotNeeded, _localizer.Format("ServerSetup.PortNotNeeded", port)))
            : RunAsync(plan, _localizer.Format("ServerSetup.PortOpen", port), cancellationToken);
    }

    /// <summary>
    /// Runs a plan and words its outcome.
    /// </summary>
    /// <param name="plan">The plan.</param>
    /// <param name="success">The message of a successful run.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How it ended.</returns>
    private async Task<ServerSetupResult> RunAsync(ServerSetupPlan plan, string success, CancellationToken cancellationToken)
    {
        if (plan.Command is null)
        {
            return new ServerSetupResult(ServerSetupOutcome.Unsupported, _localizer.Get(plan.ReasonKey ?? "ServerSetup.Unsupported"), plan.ManualCommands);
        }

        ElevatedProcessExit exit;
        try
        {
            exit = await _launcher.RunAsync(plan.Command, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return new ServerSetupResult(ServerSetupOutcome.Failed, _localizer.Format("ServerSetup.StartFailed", ex.Message), plan.ManualCommands);
        }

        if (IsDeclined(plan.Command.Elevation, exit))
        {
            return new ServerSetupResult(ServerSetupOutcome.Cancelled, _localizer.Get("ServerSetup.Cancelled"));
        }

        if (exit.ExitCode == 0)
        {
            return new ServerSetupResult(ServerSetupOutcome.Completed, success);
        }

        var message = plan.Command.Elevation == ElevationKind.WindowsUac && exit.ExitCode == ServerSetupPlanner.WINDOWS_UPDATE_MISSING
            ? _localizer.Get("ServerSetup.WindowsUpdateMissing")
            : _localizer.Format("ServerSetup.Failed", exit.ExitCode);
        return new ServerSetupResult(ServerSetupOutcome.Failed, message, plan.ManualCommands);
    }

    /// <summary>
    /// Tells whether the user dismissed the prompt for administrator rights, by the way the prompt reports it.
    /// </summary>
    /// <param name="elevation">How the command asked for the rights.</param>
    /// <param name="exit">How the command ended.</param>
    /// <returns><see langword="true"/> when the prompt was dismissed.</returns>
    private static bool IsDeclined(ElevationKind elevation, ElevatedProcessExit exit) => elevation switch
    {
        ElevationKind.WindowsUac => exit.Declined,
        ElevationKind.Polkit => exit.ExitCode == PKEXEC_DISMISSED,
        _ => exit.ExitCode != 0 && exit.StandardError.Contains(MAC_USER_CANCELED, StringComparison.Ordinal)
    };
}
