using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Diagnostics;
using RCynQ.Localization;
using RCynQ.Sync.Diagnostics.AutoFix;

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
        app.MapPost(ApiRoutes.DIAGNOSTICS_RUN, async (IDiagnosticsRunner runner, CancellationToken ct) => Results.Ok(await runner.RunAsync(ct)));
        app.MapPost(ApiRoutes.DIAGNOSTICS_FIX, ApplyFixAsync);
        app.MapGet(ApiRoutes.DIAGNOSTICS_RECENT, async (IDiagnosticRunRepository runs, int? take, CancellationToken ct) =>
            Results.Ok(await runs.GetRecentAsync(Math.Clamp(take ?? 10, 1, 100), ct)));
        return app;
    }

    /// <summary>
    /// Handles <c>POST /diagnostics/fix</c>: applies a fix the node itself proposed.
    /// </summary>
    /// <param name="request">The fix, as it came with the result of a check, and the confirmation of the user.</param>
    /// <param name="fixer">The auto-fixer.</param>
    /// <param name="issued">The registry of the fixes this node proposed.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The outcome; a fix the node did not propose is not applied.</returns>
    /// <remarks>
    /// The fix in the request is only an echo of what the node sent. It is applied when the node finds it among its own
    /// proposals; a fix with other parameters (another key, another directory) is answered as not applied.
    /// </remarks>
    private static async Task<IResult> ApplyFixAsync(ApplyFixRequest request, IAutoFixer fixer, IssuedFixRegistry issued, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (request.Fix is null)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Get("Api.NoFix")));
        }

        return issued.IsIssued(request.Fix)
            ? Results.Ok(await fixer.ApplyAsync(request.Fix, request.Confirmed, cancellationToken))
            : Results.Ok(new AutoFixResult(false, localizer.Get("Api.FixNotIssued")));
    }
}
