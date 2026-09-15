using RCynQ.Core.Events;

namespace RCynQ.Core.Abstractions.Events;

/// <summary>
/// Publishes node events to interested clients (SignalR in the service, no-op in the CLI).
/// </summary>
/// <remarks>
/// Implementations must not throw for delivery failures; events are best-effort notifications.
/// </remarks>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a link state change.
    /// </summary>
    /// <param name="linkEvent">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event was handed off.</returns>
    Task PublishLinkStateAsync(LinkStateChangedEvent linkEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a patch event.
    /// </summary>
    /// <param name="patchEvent">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event was handed off.</returns>
    Task PublishPatchAsync(PatchEvent patchEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Publishes a diagnostics event.
    /// </summary>
    /// <param name="diagnosticsEvent">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event was handed off.</returns>
    Task PublishDiagnosticsAsync(DiagnosticsEvent diagnosticsEvent, CancellationToken cancellationToken = default);
}
