using RCynQ.Core.Events;

namespace RCynQ.UI.Services.Events;

/// <summary>
/// Live node events pushed by the service over SignalR.
/// </summary>
public interface INodeEventStream : IAsyncDisposable
{
    /// <summary>Raised when a link changes state.</summary>
    event EventHandler<LinkStateChangedEvent>? LinkStateChanged;

    /// <summary>Raised when a patch changes status.</summary>
    event EventHandler<PatchEvent>? PatchChanged;

    /// <summary>Raised when a diagnostics run completes.</summary>
    event EventHandler<DiagnosticsEvent>? DiagnosticsCompleted;

    /// <summary>Raised when the repository of this node or of the node at the other end changes.</summary>
    event EventHandler<RepositoryEvent>? RepositoryChanged;

    /// <summary>Raised when the routes of a link were checked or the route in use changed.</summary>
    event EventHandler<LinkRoutesChangedEvent>? LinkRoutesChanged;

    /// <summary>Raised when the connection state changes; the argument tells whether the stream is connected.</summary>
    event EventHandler<bool>? ConnectionChanged;

    /// <summary>
    /// Gets a value indicating whether the stream is connected.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Connects to the hub; reconnects automatically afterwards.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the connection attempt.</param>
    /// <returns>A task that completes when connected.</returns>
    Task ConnectAsync(CancellationToken cancellationToken = default);
}
