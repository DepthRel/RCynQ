using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Reachability;

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
        app.MapGet(ApiRoutes.PeerPing, PingAsync);
        return app;
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
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NodeId].FirstOrDefault(), http.Request.Headers[ApiHeaders.LinkToken].FirstOrDefault());
        try
        {
            return Results.Ok(await pings.AnswerAsync(context, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            return Results.Json(new PatchRejectionResponse(ex.Reason, localizer.Describe(ex)), statusCode: PatchRejectionStatusCodes.For(ex.Reason));
        }
    }
}
