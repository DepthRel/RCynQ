using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Patch channel endpoints: receive, list, inspect, resolve, send, baseline, state and history.
/// </summary>
public static class PatchEndpoints
{
    /// <summary>
    /// The logger category.
    /// </summary>
    private const string LoggerCategory = "RCynQ.Service.Patches";

    /// <summary>
    /// Maps the patch endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapPatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.Patch, ReceiveAsync);
        app.MapGet(ApiRoutes.State, async (IPatchService patches, CancellationToken ct) => Results.Ok(await patches.GetStateAsync(ct)));
        app.MapGet(ApiRoutes.Patches, async (IPatchService patches, int? take, CancellationToken ct) => Results.Ok(await patches.GetRecentAsync(Math.Clamp(take ?? 50, 1, 500), ct)));
        app.MapGet(ApiRoutes.PatchById, async (Guid id, IPatchService patches, CancellationToken ct) =>
            await patches.GetAsync(id, ct) is { } patch ? Results.Ok(patch) : NotFound($"Patch {id} was not found."));
        app.MapGet(ApiRoutes.PatchDiff, GetDiffAsync);
        app.MapPost(ApiRoutes.ResolvePatch, ResolveAsync);
        app.MapPost(ApiRoutes.SendPatch, SendAsync);
        app.MapPost(ApiRoutes.Baseline, BaselineAsync);
        app.MapGet(ApiRoutes.History, async (ISyncHistoryRepository history, int? take, CancellationToken ct) => Results.Ok(await history.GetRecentAsync(Math.Clamp(take ?? 100, 1, 1000), ct)));
        return app;
    }

    /// <summary>
    /// Handles <c>POST /patch</c> from the peer.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="serializer">The envelope serializer.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The result.</returns>
    private static async Task<IResult> ReceiveAsync(HttpContext http, NodeConfig node, IPatchService patches, PatchEnvelopeSerializer serializer, ILoggerFactory loggers, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return Rejection(PatchRejectionReason.RoleNotAllowed, $"A {node.Role} node does not accept patches.");
        }

        var logger = loggers.CreateLogger(LoggerCategory);
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, cancellationToken);

        PatchEnvelope envelope;
        try
        {
            envelope = serializer.Deserialize(buffer.ToArray());
        }
        catch (FormatException ex)
        {
            logger.LogWarning("Refused a malformed patch envelope: {Error}", ex.Message);
            return Rejection(PatchRejectionReason.Malformed, ex.Message);
        }

        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NodeId].FirstOrDefault(), http.Request.Headers[ApiHeaders.LinkToken].FirstOrDefault());
        try
        {
            return Results.Ok(await patches.ReceiveAsync(envelope, context, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            logger.LogWarning("Refused patch {PatchId} from {Sender}: {Reason} {Error}", envelope.Id, context.SenderNodeId, ex.Reason, ex.Message);
            return Rejection(ex.Reason, ex.Message);
        }
    }

    /// <summary>
    /// Handles <c>GET /patches/{id}/diff</c>.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The diff as text or 404.</returns>
    private static async Task<IResult> GetDiffAsync(Guid id, IPatchService patches, CancellationToken cancellationToken)
    {
        try
        {
            return Results.File(await patches.ReadDiffAsync(id, cancellationToken), "text/x-diff; charset=utf-8");
        }
        catch (SyncBridgeException ex)
        {
            return NotFound(ex.Message);
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/{id}/resolve</c>.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The resolution result.</returns>
    private static async Task<IResult> ResolveAsync(Guid id, ResolvePatchRequest request, NodeConfig node, IPatchService patches, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return NotFound($"A {node.Role} node has no patches to resolve.");
        }

        try
        {
            return Results.Ok(await patches.ResolveAsync(id, request.Policy, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(ex.Message));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/send</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The send result.</returns>
    private static async Task<IResult> SendAsync(NodeConfig node, IPatchService patches, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).SendsPatches)
        {
            return NotFound($"A {node.Role} node does not send patches.");
        }

        try
        {
            return Results.Ok(await patches.SendAsync(cancellationToken));
        }
        catch (SyncBridgeException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/baseline</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The snapshot.</returns>
    private static async Task<IResult> BaselineAsync(NodeConfig node, IPatchService patches, CancellationToken cancellationToken)
    {
        if (!node.ParticipatesInPatchChannel)
        {
            return NotFound($"A {node.Role} node has no sync base.");
        }

        try
        {
            return Results.Ok(await patches.InitializeBaselineAsync(cancellationToken));
        }
        catch (SyncBridgeException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Creates a rejection response.
    /// </summary>
    /// <param name="reason">The reason.</param>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    private static IResult Rejection(PatchRejectionReason reason, string message)
        => Results.Json(new PatchRejectionResponse(reason, message), statusCode: PatchRejectionStatusCodes.For(reason));

    /// <summary>
    /// Creates a 404 response.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    private static IResult NotFound(string message) => Results.NotFound(new ErrorResponse(message));
}
