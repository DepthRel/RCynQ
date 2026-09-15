using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Api;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Maps the service lifecycle endpoints.
/// </summary>
public static class ServiceEndpoints
{
    /// <summary>
    /// Maps <c>POST /service/shutdown</c>, protected by the local API token.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The route builder.</returns>
    public static IEndpointRouteBuilder MapServiceEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.ServiceShutdown, (IHostApplicationLifetime lifetime, ILoggerFactory loggers) =>
        {
            loggers.CreateLogger(typeof(ServiceEndpoints)).LogInformation("Shutdown requested through the local API.");
            lifetime.StopApplication();
            return Results.Accepted(value: new { status = "stopping" });
        });
        return app;
    }
}
