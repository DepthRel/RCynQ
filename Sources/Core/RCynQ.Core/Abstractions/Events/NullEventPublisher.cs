using RCynQ.Core.Events;

namespace RCynQ.Core.Abstractions.Events;

/// <summary>
/// An <see cref="IEventPublisher"/> that discards every event.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static NullEventPublisher Instance { get; } = new();

    /// <inheritdoc />
    public Task PublishLinkStateAsync(LinkStateChangedEvent linkEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PublishPatchAsync(PatchEvent patchEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <inheritdoc />
    public Task PublishDiagnosticsAsync(DiagnosticsEvent diagnosticsEvent, CancellationToken cancellationToken = default) => Task.CompletedTask;
}
