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

    /// <summary>
    /// Publishes a change of the repository this node or the node at the other end works with.
    /// </summary>
    /// <remarks>
    /// A publisher that has nobody to tell (a command, a test) may leave the default, which does nothing.
    /// </remarks>
    /// <param name="repositoryEvent">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event is published.</returns>
    Task PublishRepositoryAsync(RepositoryEvent repositoryEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Publishes the outcome of a check of the routes of a link, or a change of the route in use.
    /// </summary>
    /// <remarks>
    /// A publisher that has nobody to tell (a command, a test) may leave the default, which does nothing.
    /// </remarks>
    /// <param name="routesEvent">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the event is published.</returns>
    Task PublishLinkRoutesAsync(LinkRoutesChangedEvent routesEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
