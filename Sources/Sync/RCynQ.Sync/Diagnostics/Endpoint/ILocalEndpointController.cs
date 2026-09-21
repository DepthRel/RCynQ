using RCynQ.Core.Diagnostics;

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
