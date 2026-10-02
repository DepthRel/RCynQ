using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Events;
using RCynQ.Service.Hubs;

namespace RCynQ.Service.Events;

/// <summary>
/// <see cref="IEventPublisher"/> that broadcasts node events to all SignalR clients.
/// </summary>
public sealed class SignalREventPublisher : IEventPublisher
{
    /// <summary>The hub context.</summary>
    private readonly IHubContext<SyncBridgeHub> _hub;

    /// <summary>The logger.</summary>
    private readonly ILogger<SignalREventPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalREventPublisher"/> class.
    /// </summary>
    /// <param name="hub">The hub context.</param>
    /// <param name="logger">The logger.</param>
    public SignalREventPublisher(IHubContext<SyncBridgeHub> hub, ILogger<SignalREventPublisher> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task PublishLinkStateAsync(LinkStateChangedEvent linkEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Link {Link} via {Transport} is {Status}: {Message}", linkEvent.State.LinkName, linkEvent.State.Kind, linkEvent.State.Status, linkEvent.State.Message);
        return SendAsync(NodeEventNames.LINK_STATE_CHANGED, linkEvent, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishPatchAsync(PatchEvent patchEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Patch {PatchId} ({Direction}) is {Status}; applied {Applied}, conflicts {Conflicts}. {Message}",
            patchEvent.PatchId, patchEvent.Direction, patchEvent.Status, patchEvent.AppliedFileCount, patchEvent.ConflictCount, patchEvent.Message);
        return SendAsync(NodeEventNames.PATCH_EVENT, patchEvent, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishDiagnosticsAsync(DiagnosticsEvent diagnosticsEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Diagnostics for {Role}: {Failed} of {Total} checks failed.", diagnosticsEvent.Role, diagnosticsEvent.Results.Count(result => !result.Passed), diagnosticsEvent.Results.Count);
        return SendAsync(NodeEventNames.DIAGNOSTICS_EVENT, diagnosticsEvent, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishRepositoryAsync(RepositoryEvent repositoryEvent, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Repository of this node: {Repository}; of the node at the other end: {PeerRepository}.", repositoryEvent.Repository, repositoryEvent.PeerRepository);
        return SendAsync(NodeEventNames.REPOSITORY_EVENT, repositoryEvent, cancellationToken);
    }

    /// <inheritdoc />
    public Task PublishLinkRoutesAsync(LinkRoutesChangedEvent routesEvent, CancellationToken cancellationToken = default)
    {
        if (routesEvent.Switched && routesEvent.Routes.Routes.FirstOrDefault(route => route.IsActive) is { } active)
        {
            _logger.LogInformation("Link {LinkId} now goes through route {Route} ({Host}).", routesEvent.Routes.LinkId, active.Name, active.Host);
        }

        return SendAsync(NodeEventNames.LINK_ROUTES_CHANGED, routesEvent, cancellationToken);
    }

    /// <summary>
    /// Broadcasts an event, logging delivery failures instead of throwing.
    /// </summary>
    /// <param name="method">The client method name.</param>
    /// <param name="payload">The event.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task SendAsync(string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            await _hub.Clients.All.SendAsync(method, payload, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Publishing {Method} to SignalR clients failed.", method);
        }
    }
}
