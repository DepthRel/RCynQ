using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// The result of starting or restarting the local service.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">A user-facing description.</param>
/// <param name="ExitCode">The exit code when the service process exited, otherwise <see langword="null"/>.</param>
public sealed record ServiceStartResult(ServiceStartOutcome Outcome, string Message, int? ExitCode = null)
{
    /// <summary>
    /// The exit code of RCynQ.Service for an invalid configuration.
    /// </summary>
    public const int CONFIGURATION_ERROR_EXIT_CODE = 2;

    /// <summary>
    /// Gets a value indicating whether the service answers.
    /// </summary>
    public bool IsRunning => Outcome is ServiceStartOutcome.Started or ServiceStartOutcome.AlreadyRunning;

    /// <summary>
    /// Gets a value indicating whether the configuration is missing or was rejected by the service.
    /// </summary>
    public bool IsConfigurationError => Outcome == ServiceStartOutcome.NotConfigured ||
        (Outcome == ServiceStartOutcome.Failed && ExitCode == CONFIGURATION_ERROR_EXIT_CODE);
}
