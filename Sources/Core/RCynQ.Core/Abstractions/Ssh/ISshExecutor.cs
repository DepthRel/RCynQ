using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Ssh;

/// <summary>
/// Executes remote commands over SSH.
/// </summary>
/// <remarks>
/// The remote command is passed to <c>ssh</c> as a single argument; implementations never build a local
/// shell command line.
/// </remarks>
public interface ISshExecutor
{
    /// <summary>
    /// Executes a remote command, optionally through jump hosts.
    /// </summary>
    /// <param name="target">The SSH endpoint.</param>
    /// <param name="jumps">The ordered jump hosts; empty for none.</param>
    /// <param name="remoteCommand">The remote command.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <returns>The captured result.</returns>
    /// <exception cref="ArgumentException"><paramref name="remoteCommand"/> is blank or contains control characters.</exception>
    /// <exception cref="SshExecutionException">SSH could not be executed.</exception>
    Task<SshResult> ExecuteAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string remoteCommand, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a remote command that logs in to the target with the given key (a one-time enrollment key); the jump
    /// hosts are reached with the key of the node.
    /// </summary>
    /// <param name="target">The SSH endpoint.</param>
    /// <param name="jumps">The ordered jump hosts; empty for none.</param>
    /// <param name="identityFile">The private key file for the target.</param>
    /// <param name="remoteCommand">The remote command.</param>
    /// <param name="cancellationToken">A token to cancel the execution.</param>
    /// <returns>The captured result.</returns>
    /// <exception cref="ArgumentException">The key file or the command cannot be passed safely.</exception>
    /// <exception cref="SshExecutionException">SSH could not be executed.</exception>
    Task<SshResult> ExecuteWithKeyAsync(SshEndpoint target, IReadOnlyList<SshEndpoint> jumps, string identityFile, string remoteCommand, CancellationToken cancellationToken = default);
}
