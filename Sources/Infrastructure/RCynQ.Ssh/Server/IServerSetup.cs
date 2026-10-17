namespace RCynQ.Ssh.Server;

/// <summary>
/// Prepares the computer to accept neighbours, asking the user for administrator rights: installs and starts the SSH
/// server, or lets the TLS port of the node through the firewall.
/// </summary>
public interface IServerSetup
{
    /// <summary>
    /// Installs, starts and opens the SSH server.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How it ended.</returns>
    Task<ServerSetupResult> InstallSshServerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lets the TLS port of the node through the firewall.
    /// </summary>
    /// <param name="port">The port, 1–65535.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>How it ended.</returns>
    Task<ServerSetupResult> OpenEndpointPortAsync(int port, CancellationToken cancellationToken = default);
}
