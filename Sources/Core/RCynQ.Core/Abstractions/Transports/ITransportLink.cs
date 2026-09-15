using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;

namespace RCynQ.Core.Abstractions.Transports;

/// <summary>
/// A runtime transport link between two nodes.
/// </summary>
/// <remarks>
/// The contract is intentionally transport-agnostic: both the direct link and
/// the AmneziaWG link implement this interface.
/// </remarks>
public interface ITransportLink
{
    /// <summary>
    /// Gets the transport kind of the link.
    /// </summary>
    TransportKind Kind { get; }

    /// <summary>
    /// Starts the link and makes it ready for traffic.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the startup.</param>
    /// <returns>A task that completes when the link is ready.</returns>
    /// <exception cref="SyncBridgeException">The link could not be started.</exception>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the link and releases all resources.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the shutdown.</param>
    /// <returns>A task that completes when the link has been stopped.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current runtime state of the link.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the current link state.</returns>
    Task<LinkState> GetStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs diagnostics against the link and returns the first meaningful result.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the diagnostic result.</returns>
    Task<DiagnosticResult> DiagnoseAsync(CancellationToken cancellationToken = default);
}
