using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Maintenance;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Endpoints that maintain the stored data of the node.
/// </summary>
public static class DataEndpoints
{
    /// <summary>
    /// Maps <c>POST /data/cleanup</c>, protected by the local API token.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.DATA_CLEANUP, CleanupAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /data/cleanup</c>: removes everything that is finished, whatever its age.
    /// </summary>
    /// <param name="retention">The retention service.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>What was removed.</returns>
    private static async Task<IResult> CleanupAsync(DataRetentionService retention, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            var report = await retention.CleanAsync(TimeSpan.Zero, cancellationToken);
            loggers.CreateLogger(typeof(DataEndpoints)).LogInformation(
                "Data cleanup requested through the local API removed {Patches} patch(es), {History} history entries, {Runs} diagnostics run(s), {Backups} backup(s), {Temporary} temporary item(s); {Bytes} bytes freed.",
                report.Patches, report.HistoryEntries, report.DiagnosticRuns, report.Backups, report.TemporaryItems, report.FreedBytes);
            return Results.Ok(report);
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }
}
