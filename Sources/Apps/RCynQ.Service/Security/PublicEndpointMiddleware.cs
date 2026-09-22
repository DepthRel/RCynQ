using Microsoft.AspNetCore.Http;
using RCynQ.Core.Api;
using RCynQ.Localization;

namespace RCynQ.Service.Security;

/// <summary>
/// Restricts the direct endpoint listener to the routes a neighbour needs.
/// </summary>
/// <remarks>
/// The local API is reachable with the local API token and is meant for the UI and the CLI on this computer only.
/// The direct endpoint is open to the network, so requests arriving on its port may only receive a patch
/// (the <c>/patch/sessions</c> routes, authenticated by the link token and the manifest signature) or read the health of the node.
/// Everything else is answered with 404, as if the route did not exist.
/// </remarks>
public sealed class PublicEndpointMiddleware
{
    /// <summary>The next middleware.</summary>
    private readonly RequestDelegate _next;

    /// <summary>The port of the direct endpoint.</summary>
    private readonly int _port;

    /// <summary>
    /// Initializes a new instance of the <see cref="PublicEndpointMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="options">The direct endpoint settings.</param>
    public PublicEndpointMiddleware(RequestDelegate next, PublicEndpointOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _next = next;
        _port = options.Port;
    }

    /// <summary>
    /// Handles one request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Connection.LocalPort != _port)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path;
        var allowed = ((HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method))
                && path.StartsWithSegments(ApiRoutes.PatchSessionPrefix, StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsPost(context.Request.Method) && path.Equals(ApiRoutes.GitBundle, StringComparison.OrdinalIgnoreCase))
            || (HttpMethods.IsGet(context.Request.Method) && path.Equals(ApiRoutes.Health, StringComparison.OrdinalIgnoreCase));
        if (!allowed)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.WriteAsJsonAsync(new ErrorResponse(Texts.Get("Api.RouteNotPublic")));
            return;
        }

        await _next(context);
    }
}
