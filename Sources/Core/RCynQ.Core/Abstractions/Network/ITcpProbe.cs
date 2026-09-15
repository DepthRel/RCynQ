namespace RCynQ.Core.Abstractions.Network;

/// <summary>
/// Checks TCP reachability.
/// </summary>
public interface ITcpProbe
{
    /// <summary>
    /// Tries to open a TCP connection to the given endpoint.
    /// </summary>
    /// <param name="host">The host name or address.</param>
    /// <param name="port">The TCP port.</param>
    /// <param name="timeout">The connect timeout.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns><see langword="true"/> when a connection could be established; otherwise <see langword="false"/>.</returns>
    Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default);
}
