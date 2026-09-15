using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Ssh;

/// <summary>
/// Manages SSH local port forwards (<c>ssh -N -L</c>).
/// </summary>
/// <remarks>
/// Forwards are long-running background processes bound to the loopback interface only and must be
/// stoppable by local port without leaking child processes.
/// </remarks>
public interface ILocalForwardManager
{
    /// <summary>
    /// Starts a local forward <c>127.0.0.1:localPort → remoteHost:remotePort</c> as seen from <paramref name="target"/>.
    /// </summary>
    /// <param name="target">The SSH endpoint that opens the forwarded connection.</param>
    /// <param name="jumps">The ordered jump hosts; empty for a direct connection.</param>
    /// <param name="localPort">The local loopback port, or <c>0</c> for an ephemeral port.</param>
    /// <param name="remoteHost">The host the target connects to, usually <c>127.0.0.1</c>.</param>
    /// <param name="remotePort">The port the target connects to.</param>
    /// <param name="cancellationToken">A token to cancel the startup.</param>
    /// <returns>The local port the forward is bound to.</returns>
    /// <exception cref="ArgumentException">An argument is invalid.</exception>
    /// <exception cref="SshExecutionException">The forward could not be started.</exception>
    Task<int> StartAsync(
        SshEndpoint target,
        IReadOnlyList<SshEndpoint> jumps,
        int localPort,
        string remoteHost,
        int remotePort,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the forward bound to the given local port. Does nothing when no forward is registered.
    /// </summary>
    /// <param name="localPort">The local port.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the forward has been stopped.</returns>
    Task StopAsync(int localPort, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks whether a forward is running for the given local port.
    /// </summary>
    /// <param name="localPort">The local port.</param>
    /// <returns><see langword="true"/> when a forward is running; otherwise <see langword="false"/>.</returns>
    bool IsRunning(int localPort);
}
