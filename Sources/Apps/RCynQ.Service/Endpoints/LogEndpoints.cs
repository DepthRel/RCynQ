using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Service.Logging;
using Serilog.Events;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Log endpoints.
/// </summary>
public static class LogEndpoints
{
    /// <summary>
    /// Maps <c>GET /logs?level=Warning&amp;take=500</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapLogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Logs, (InMemoryLogSink sink, string? level, int? take) =>
        {
            var minimum = Enum.TryParse<LogEventLevel>(level, ignoreCase: true, out var parsed) ? parsed : LogEventLevel.Information;
            return Results.Ok(sink.GetEntries(minimum, Math.Clamp(take ?? 500, 1, InMemoryLogSink.Capacity)));
        });
        return app;
    }
}
