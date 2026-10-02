using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Sync.Configuration;
using RCynQ.Transports.Abstractions.Management;
using RCynQ.Localization;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Chain link and transport endpoints.
/// </summary>
public static class LinkEndpoints
{
    /// <summary>
    /// Maps the link endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapLinkEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Links, async (IChainLinkRepository links, CancellationToken ct) =>
            Results.Ok((await links.GetAllAsync(ct)).Select(LinkDto.FromConfig).ToArray()));
        app.MapPost(ApiRoutes.Links, UpsertAsync);
        app.MapDelete(ApiRoutes.LinkById, DeleteAsync);
        app.MapGet(ApiRoutes.LinkStatus, async (ILinkManager manager, CancellationToken ct) => Results.Ok(await manager.GetStatusAsync(ct)));
        app.MapPost(ApiRoutes.LinkUp, (Guid id, ILinkManager manager, ILocalizer localizer, CancellationToken ct) => RunAsync(() => manager.StartAsync(id, ct), localizer));
        app.MapPost(ApiRoutes.LinkDown, (Guid id, ILinkManager manager, ILocalizer localizer, CancellationToken ct) => RunAsync(() => manager.StopAsync(id, ct), localizer));
        return app;
    }

    /// <summary>
    /// Handles <c>POST /links</c>.
    /// </summary>
    /// <param name="request">The link settings.</param>
    /// <param name="links">The store that saves a typed link without erasing what is known about its peer.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The stored link.</returns>
    private static async Task<IResult> UpsertAsync(ChainLinkOptions request, ConfiguredLinkStore links, ILocalizer localizer, CancellationToken cancellationToken)
    {
        ChainLinkConfig link;
        try
        {
            link = SyncBridgeOptionsValidator.ToChainLink(request);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }

        try
        {
            return Results.Ok(LinkDto.FromConfig(await links.SaveAsync(link, cancellationToken)));
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new ErrorResponse(localizer.Format("Api.LinkExists", link.Name)));
        }
    }

    /// <summary>
    /// Handles <c>DELETE /links/{id}</c>: removes the link and stops its transport when this service started it.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="manager">The link manager.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>204, or 404 when there is no such link.</returns>
    private static async Task<IResult> DeleteAsync(Guid id, IChainLinkRepository links, ILinkManager manager, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!await links.DeleteAsync(id, cancellationToken))
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Api.LinkNotFound", id)));
        }

        try
        {
            // Without this a tunnel of the removed link would stay up until the service stops.
            await manager.ReleaseAsync(id, cancellationToken);
        }
        catch (SyncBridgeException ex)
        {
            // The link is removed either way; its transport is stopped again when the service shuts down.
            loggers.CreateLogger(typeof(LinkEndpoints)).LogWarning("The transport of the removed link {LinkId} could not be stopped: {Error}", id, ex.Message);
        }

        return Results.NoContent();
    }

    /// <summary>
    /// Runs a link operation and maps unknown links to 404 and transport failures to 400.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <returns>The result.</returns>
    private static async Task<IResult> RunAsync(Func<Task<Core.State.LinkState>> operation, ILocalizer localizer)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (SyncBridgeException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }
}
