using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;

namespace RCynQ.Transports.Abstractions.Management;

/// <summary>
/// Manages the runtime transport links of the configured chain.
/// </summary>
public interface ILinkManager
{
    /// <summary>
    /// Starts every configured link, with failover.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting states.</returns>
    Task<IReadOnlyList<LinkState>> StartAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops every active link.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting states.</returns>
    Task<IReadOnlyList<LinkState>> StopAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts one link, with failover.
    /// </summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state.</returns>
    /// <exception cref="KeyNotFoundException">The link is not configured.</exception>
    Task<LinkState> StartAsync(Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops one link. A link that is not active in this process is stopped through its primary transport,
    /// so a tunnel started by another process can be brought down.
    /// </summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state.</returns>
    /// <exception cref="KeyNotFoundException">The link is not configured.</exception>
    Task<LinkState> StopAsync(Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the state of every configured link and publishes changes.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The states in chain order.</returns>
    Task<IReadOnlyList<LinkState>> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Keeps a usable primary link, otherwise restarts the link trying the primary and then the fallback transports.
    /// </summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resulting state.</returns>
    /// <exception cref="KeyNotFoundException">The link is not configured.</exception>
    Task<LinkState> TryFailoverAsync(Guid linkId, CancellationToken cancellationToken = default);
}
