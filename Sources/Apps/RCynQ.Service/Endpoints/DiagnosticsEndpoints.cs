using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Diagnostics;
using RCynQ.Localization;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Diagnostics endpoints.
/// </summary>
public static class DiagnosticsEndpoints
{
    /// <summary>
    /// Maps the diagnostics endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapDiagnosticsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.DiagnosticsRun, async (IDiagnosticsRunner runner, CancellationToken ct) => Results.Ok(await runner.RunAsync(ct)));
        app.MapPost(ApiRoutes.DiagnosticsFix, async (ApplyFixRequest request, IAutoFixer fixer, CancellationToken ct) =>
            request.Fix is null
                ? Results.BadRequest(new ErrorResponse(Texts.Get("Api.NoFix")))
                : Results.Ok(await fixer.ApplyAsync(request.Fix, request.Confirmed, ct)));
        app.MapGet(ApiRoutes.DiagnosticsRecent, async (IDiagnosticRunRepository runs, int? take, CancellationToken ct) =>
            Results.Ok(await runs.GetRecentAsync(Math.Clamp(take ?? 10, 1, 100), ct)));
        return app;
    }
}
