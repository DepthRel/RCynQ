using RCynQ.Core.Exceptions;

namespace RCynQ.Transports.AmneziaWG.Cli;

/// <summary>
/// Controls AmneziaWG interfaces.
/// </summary>
public interface IAmneziaWgCli
{
    /// <summary>
    /// Brings an interface up.
    /// </summary>
    /// <param name="configPath">The absolute path of the <c>.conf</c> file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status after starting.</returns>
    /// <exception cref="ArgumentException">The configuration path is invalid.</exception>
    /// <exception cref="SyncBridgeException">The interface could not be started.</exception>
    Task<AmneziaWgStatus> StartAsync(string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings an interface down. Does nothing when it is not up.
    /// </summary>
    /// <param name="configPath">The absolute path of the <c>.conf</c> file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the interface is down.</returns>
    /// <exception cref="ArgumentException">The configuration path is invalid.</exception>
    /// <exception cref="SyncBridgeException">The interface could not be stopped.</exception>
    Task StopAsync(string configPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the interface status.
    /// </summary>
    /// <param name="configPath">The absolute path of the <c>.conf</c> file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status; a missing CLI is reported as not running.</returns>
    /// <exception cref="ArgumentException">The configuration path is invalid.</exception>
    Task<AmneziaWgStatus> GetStatusAsync(string configPath, CancellationToken cancellationToken = default);
}
