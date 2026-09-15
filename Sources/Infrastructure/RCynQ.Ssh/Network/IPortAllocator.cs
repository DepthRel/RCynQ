using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Ssh.Commands;

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
