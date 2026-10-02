using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Reachability;
using RCynQ.Sync.Repositories;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// The endpoints the counterpart endpoint of the chain asks, besides the ones that carry patches.
/// </summary>
public static class PeerEndpoints
{
    /// <summary>
    /// Maps the peer endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The same builder.</returns>
    public static IEndpointRouteBuilder MapPeerEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.PEER_PING, PingAsync);
        app.MapGet(ApiRoutes.PEER_REPOSITORIES, RepositoriesAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>GET /peer/repositories</c>: tells a Workstation which repositories this Origin offers.
    /// </summary>
    /// <param name="http">The HTTP context with the headers of the asking node.</param>
    /// <param name="repositories">The repository operations.</param>
    /// <param name="localizer">The localizer that words the answer for the peer.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The names of the repositories, or the rejection.</returns>
    private static async Task<IResult> RepositoriesAsync(HttpContext http, RepositoryService repositories, ILocalizer localizer, CancellationToken cancellationToken)
    {
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NODE_ID].FirstOrDefault(), http.Request.Headers[ApiHeaders.LINK_TOKEN].FirstOrDefault());
        try
        {
            return Results.Ok(await repositories.AnswerPeerAsync(context, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            return Results.Json(new PatchRejectionResponse(ex.Reason, localizer.Describe(ex)), statusCode: PatchRejectionStatusCodes.For(ex.Reason));
        }
    }

    /// <summary>
    /// Handles <c>GET /peer/ping</c>: tells the counterpart endpoint that this node is here and would take a patch from it.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="pings">Answers the ping.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The answer, or the rejection with the reason a patch of the same sender would be refused for.</returns>
    private static async Task<IResult> PingAsync(HttpContext http, PeerPingService pings, ILocalizer localizer, CancellationToken cancellationToken)
    {
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NODE_ID].FirstOrDefault(), http.Request.Headers[ApiHeaders.LINK_TOKEN].FirstOrDefault());
        try
        {
            return Results.Ok(await pings.AnswerAsync(context, http.Request.Headers[ApiHeaders.REPOSITORY].FirstOrDefault(), cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            return Results.Json(new PatchRejectionResponse(ex.Reason, localizer.Describe(ex)), statusCode: PatchRejectionStatusCodes.For(ex.Reason));
        }
    }
}
