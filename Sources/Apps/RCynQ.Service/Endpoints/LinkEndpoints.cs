using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Transports.Abstractions.Management;

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
        app.MapDelete(ApiRoutes.LinkById, async (Guid id, IChainLinkRepository links, CancellationToken ct) =>
            await links.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound(new ErrorResponse($"Link {id} was not found.")));
        app.MapGet(ApiRoutes.LinkStatus, async (ILinkManager manager, CancellationToken ct) => Results.Ok(await manager.GetStatusAsync(ct)));
        app.MapPost(ApiRoutes.LinkUp, (Guid id, ILinkManager manager, CancellationToken ct) => RunAsync(() => manager.StartAsync(id, ct)));
        app.MapPost(ApiRoutes.LinkDown, (Guid id, ILinkManager manager, CancellationToken ct) => RunAsync(() => manager.StopAsync(id, ct)));
        return app;
    }

    /// <summary>
    /// Handles <c>POST /links</c>.
    /// </summary>
    /// <param name="request">The link settings.</param>
    /// <param name="links">The link repository.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The stored link.</returns>
    private static async Task<IResult> UpsertAsync(ChainLinkOptions request, IChainLinkRepository links, CancellationToken cancellationToken)
    {
        ChainLinkConfig link;
        try
        {
            link = SyncBridgeOptionsValidator.ToChainLink(request);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }

        try
        {
            return Results.Ok(LinkDto.FromConfig(await links.UpsertAsync(link, cancellationToken)));
        }
        catch (DbUpdateException)
        {
            return Results.Conflict(new ErrorResponse($"A link named '{link.Name}' already exists."));
        }
    }

    /// <summary>
    /// Runs a link operation and maps unknown links to 404.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns>The result.</returns>
    private static async Task<IResult> RunAsync(Func<Task<Core.State.LinkState>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new ErrorResponse(ex.Message));
        }
    }
}
