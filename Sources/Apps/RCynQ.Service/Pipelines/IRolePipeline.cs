using RCynQ.Core.Api;
using RCynQ.Core.Enums;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// The background behaviour of the service for one node role.
/// </summary>
public interface IRolePipeline
{
    /// <summary>
    /// Gets the role.
    /// </summary>
    NodeRole Role { get; }

    /// <summary>
    /// Gets the capabilities enabled by the pipeline.
    /// </summary>
    RoleCapabilities Capabilities { get; }

    /// <summary>
    /// Starts the pipeline. Implementations must not block service start on network operations.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the start.</param>
    /// <returns>A task that completes when the pipeline started.</returns>
    Task StartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stops the pipeline.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the stop.</param>
    /// <returns>A task that completes when the pipeline stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken);
}
