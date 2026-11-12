namespace RCynQ.Cli.Services;

/// <summary>
/// Tells whether the service of a node runs on this computer.
/// </summary>
public interface ILocalServiceProbe
{
    /// <summary>
    /// Asks the loopback port of the local service whether it answers.
    /// </summary>
    /// <param name="port">The port of the local API of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns><see langword="true"/> when something answers there.</returns>
    Task<bool> IsRunningAsync(int port, CancellationToken cancellationToken);
}
