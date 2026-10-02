using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Health endpoint.
/// </summary>
public static class HealthEndpoints
{
    /// <summary>
    /// Maps <c>GET /health</c>, the only unauthenticated read endpoint. It reveals no configuration beyond the role.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.HEALTH, (NodeConfig node) => Results.Ok(new { status = "ok", role = node.Role.ToString() }));
        return app;
    }
}
