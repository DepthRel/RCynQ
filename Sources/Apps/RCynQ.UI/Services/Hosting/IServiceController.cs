using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Starts, restarts and stops the local RCynQ service on behalf of the UI.
/// </summary>
public interface IServiceController
{
    /// <summary>
    /// Raised when <see cref="Status"/> changes; may be raised on a background thread.
    /// </summary>
    event EventHandler<ServiceControllerStatus>? StatusChanged;

    /// <summary>Gets the configuration file the service runs with.</summary>
    string ConfigPath { get; }

    /// <summary>Gets the current status.</summary>
    ServiceControllerStatus Status { get; }

    /// <summary>Gets a value indicating whether the running service was started by this UI.</summary>
    bool IsManaged { get; }

    /// <summary>
    /// Uses an answering service or starts one and waits until it answers.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    Task<ServiceStartResult> EnsureRunningAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service started by this UI and starts it again, for example after a role change.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; <see cref="ServiceStartOutcome.NotManaged"/> for a service started elsewhere.</returns>
    Task<ServiceStartResult> RestartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the service started by this UI gracefully (closing its transports) and kills it after a timeout.
    /// A service started elsewhere is left running.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the service stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}
