using System.Net;
using System.Net.Sockets;

namespace RCynQ.Ssh.Network;

/// <summary>
/// <see cref="IPortAllocator"/> that asks the operating system for an ephemeral port.
/// </summary>
public sealed class LoopbackPortAllocator : IPortAllocator
{
    /// <inheritdoc />
    public int AllocateLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }
}
