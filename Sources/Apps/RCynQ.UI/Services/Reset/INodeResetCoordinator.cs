using RCynQ.Core.Config;

namespace RCynQ.UI.Services.Reset;

/// <summary>
/// Resets the node the application runs: it takes the lines of the node out of <c>authorized_keys</c> while its service
/// still runs, stops the service and removes the configuration and the data.
/// </summary>
public interface INodeResetCoordinator
{
    /// <summary>
    /// Gets a value indicating whether the node can be reset from here: only the service this application started can be
    /// stopped by it.
    /// </summary>
    bool CanReset { get; }

    /// <summary>
    /// Works out what a reset removes, without removing anything.
    /// </summary>
    /// <returns>The plan, or <see langword="null"/> when the configuration cannot be read.</returns>
    NodeResetPlan? Plan();

    /// <summary>
    /// Resets the node.
    /// </summary>
    /// <param name="plan">The plan the user confirmed.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>What the reset did. When data is left, the configuration stays and the service is not running.</returns>
    Task<NodeResetOutcome> ResetAsync(NodeResetPlan plan, CancellationToken cancellationToken = default);
}
