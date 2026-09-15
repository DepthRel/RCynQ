using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.Diagnostics.Endpoint;

/// <summary>
/// Restarts the local loopback endpoint.
/// </summary>
public interface ILocalEndpointController
{
    /// <summary>
    /// Restarts the endpoint.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The outcome.</returns>
    Task<AutoFixResult> RestartAsync(CancellationToken cancellationToken);
}
