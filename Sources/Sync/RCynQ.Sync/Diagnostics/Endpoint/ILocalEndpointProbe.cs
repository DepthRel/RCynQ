using System.Text.RegularExpressions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Git.Commands;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Security;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// Probes the local loopback service endpoint.
/// </summary>
public interface ILocalEndpointProbe
{
    /// <summary>
    /// Checks <c>GET http://127.0.0.1:&lt;port&gt;/health</c>.
    /// </summary>
    /// <param name="port">The port.</param>
    /// <param name="cancellationToken">A token to cancel the probe.</param>
    /// <returns><see langword="true"/> when the endpoint answers successfully.</returns>
    Task<bool> IsHealthyAsync(int port, CancellationToken cancellationToken = default);
}
