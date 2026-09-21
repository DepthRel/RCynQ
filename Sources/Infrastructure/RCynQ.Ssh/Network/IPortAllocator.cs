namespace RCynQ.Ssh.Network;

/// <summary>
/// Allocates free loopback ports.
/// </summary>
public interface IPortAllocator
{
    /// <summary>
    /// Finds a currently free TCP port on <c>127.0.0.1</c>.
    /// </summary>
    /// <returns>The port.</returns>
    int AllocateLoopbackPort();
}
