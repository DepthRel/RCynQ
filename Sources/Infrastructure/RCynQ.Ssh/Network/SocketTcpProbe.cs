using System.Net.Sockets;
using RCynQ.Core.Abstractions.Network;

namespace RCynQ.Ssh.Network;

/// <summary>
/// <see cref="ITcpProbe"/> using <see cref="TcpClient"/>.
/// </summary>
public sealed class SocketTcpProbe : ITcpProbe
{
    /// <inheritdoc />
    public async Task<bool> CanConnectAsync(string host, int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(host, port, timeoutSource.Token);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }
}
