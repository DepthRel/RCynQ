namespace RCynQ.Ssh.Server;

/// <summary>
/// Runs a command that asks the user for administrator rights.
/// </summary>
public interface IElevatedProcessLauncher
{
    /// <summary>
    /// Runs the command and waits for it.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How the command ended.</returns>
    /// <exception cref="InvalidOperationException">The command could not be started.</exception>
    Task<ElevatedProcessExit> RunAsync(ElevatedCommand command, CancellationToken cancellationToken = default);
}
