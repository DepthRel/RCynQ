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
using RCynQ.Localization;
using RCynQ.Sync.Services;
using RCynQ.Sync.Transfer;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Patch channel endpoints: receive (session, content, commit), list, inspect, resolve, send, baseline, state and history.
/// </summary>
public static class PatchEndpoints
{
    /// <summary>
    /// The logger category.
    /// </summary>
    private const string LOGGER_CATEGORY = "RCynQ.Service.Patches";

    /// <summary>
    /// Maps the patch endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapPatchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.PATCH_SESSIONS, OpenSessionAsync);
        app.MapPut(ApiRoutes.PATCH_SESSION_CONTENT, ReceiveContentAsync);
        app.MapPost(ApiRoutes.PATCH_SESSION_COMMIT, CommitAsync);
        app.MapGet(ApiRoutes.STATE, GetStateAsync);
        app.MapGet(ApiRoutes.STATE_SUMMARY, GetStateSummaryAsync);
        app.MapGet(ApiRoutes.PATCHES, async (IPatchService patches, int? skip, int? take, CancellationToken ct) =>
            Results.Ok(await patches.GetPageAsync(Math.Max(skip ?? 0, 0), Math.Clamp(take ?? 50, 1, ApiLimits.MAX_PATCHES_PAGE), ct)));
        app.MapGet(ApiRoutes.PATCH_BY_ID, async (Guid id, IPatchService patches, ILocalizer localizer, CancellationToken ct) =>
            await patches.GetAsync(id, ct) is { } patch ? Results.Ok(patch) : NotFound(localizer.Format("Patch.NotFound", id)));
        app.MapGet(ApiRoutes.PATCH_DIFF, GetDiffAsync);
        app.MapPost(ApiRoutes.RESOLVE_PATCH, ResolveAsync);
        app.MapPost(ApiRoutes.SEND_PATCH, SendAsync);
        app.MapPost(ApiRoutes.BASELINE, BaselineAsync);
        app.MapGet(ApiRoutes.HISTORY, async (ISyncHistoryRepository history, int? take, CancellationToken ct) => Results.Ok(await history.GetRecentAsync(Math.Clamp(take ?? 100, 1, 1000), ct)));
        return app;
    }

    /// <summary>
    /// The largest uncompressed manifest a peer may send.
    /// </summary>
    public const long MAX_MANIFEST_BYTES = 256L * 1024 * 1024;

    /// <summary>
    /// Handles <c>POST /patch/sessions</c> from the peer: opens (or resumes) the session with the gzip-compressed signed manifest.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="serializer">The envelope serializer.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The session with the hashes of the content the receiver still lacks.</returns>
    private static async Task<IResult> OpenSessionAsync(HttpContext http, NodeConfig node, IPatchService patches, PatchEnvelopeSerializer serializer, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return Rejection(PatchRejectionReason.RoleNotAllowed, localizer.Format("Receive.RoleNotAllowed", node.Role));
        }

        var logger = loggers.CreateLogger(LOGGER_CATEGORY);
        PatchEnvelope envelope;
        try
        {
            envelope = serializer.Deserialize(await ReadManifestAsync(http.Request, cancellationToken));
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            logger.LogWarning("Refused a malformed patch envelope: {Error}", ex.Message);
            return Rejection(PatchRejectionReason.Malformed, localizer.Describe(ex));
        }

        var context = ContextOf(http);
        try
        {
            return Results.Ok(await patches.OpenSessionAsync(envelope, context, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            logger.LogWarning("Refused patch {PatchId} from {Sender}: {Reason} {Error}", envelope.Id, context.SenderNodeId, ex.Reason, ex.Message);
            return Rejection(ex.Reason, localizer.Describe(ex));
        }
    }

    /// <summary>
    /// Handles <c>PUT /patch/sessions/{id}/content/{sha256}</c> from the peer: stores the gzip-compressed content of one file.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="sha256">The SHA-256 hash of the content.</param>
    /// <param name="http">The HTTP context.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>An empty success result, or the rejection.</returns>
    private static async Task<IResult> ReceiveContentAsync(Guid id, string sha256, HttpContext http, NodeConfig node, IPatchService patches, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return Rejection(PatchRejectionReason.RoleNotAllowed, localizer.Format("Receive.RoleNotAllowed", node.Role));
        }

        var logger = loggers.CreateLogger(LOGGER_CATEGORY);
        var context = ContextOf(http);
        try
        {
            await using var body = IsCompressed(http.Request) ? GzipCodec.Decompress(http.Request.Body) : http.Request.Body;
            await patches.ReceiveContentAsync(id, sha256, body, context, cancellationToken);
            return Results.NoContent();
        }
        catch (PatchRejectedException ex)
        {
            logger.LogWarning("Refused content {Hash} of patch {PatchId} from {Sender}: {Reason} {Error}", sha256, id, context.SenderNodeId, ex.Reason, ex.Message);
            return Rejection(ex.Reason, localizer.Describe(ex));
        }
    }

    /// <summary>
    /// Handles <c>POST /patch/sessions/{id}/commit</c> from the peer: applies the patch once all its content is stored.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="http">The HTTP context.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The processing result, or the rejection.</returns>
    private static async Task<IResult> CommitAsync(Guid id, HttpContext http, NodeConfig node, IPatchService patches, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return Rejection(PatchRejectionReason.RoleNotAllowed, localizer.Format("Receive.RoleNotAllowed", node.Role));
        }

        var logger = loggers.CreateLogger(LOGGER_CATEGORY);
        var context = ContextOf(http);
        try
        {
            return Results.Ok(await patches.CommitAsync(id, context, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            logger.LogWarning("Refused to apply patch {PatchId} from {Sender}: {Reason} {Error}", id, context.SenderNodeId, ex.Reason, ex.Message);
            return Rejection(ex.Reason, localizer.Describe(ex));
        }
    }

    /// <summary>
    /// Reads the transport-level facts a receiver validates.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <returns>The sender node header and the link token.</returns>
    private static PatchReceiveContext ContextOf(HttpContext http)
        => new(http.Request.Headers[ApiHeaders.NODE_ID].FirstOrDefault(), http.Request.Headers[ApiHeaders.LINK_TOKEN].FirstOrDefault());

    /// <summary>
    /// Checks whether a request body is gzip-compressed.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <returns><see langword="true"/> when the <c>Content-Encoding</c> header says gzip.</returns>
    private static bool IsCompressed(HttpRequest request)
        => request.Headers.ContentEncoding.Any(value => string.Equals(value?.Trim(), GzipCodec.CONTENT_ENCODING, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads the manifest of a request into memory, decompressing it when needed, within <see cref="MAX_MANIFEST_BYTES"/>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The uncompressed JSON bytes.</returns>
    /// <exception cref="InvalidDataException">The body is not valid gzip data or larger than the limit.</exception>
    private static async Task<byte[]> ReadManifestAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (IsCompressed(request))
        {
            return await GzipCodec.DecompressAsync(request.Body, MAX_MANIFEST_BYTES, cancellationToken);
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.Length <= MAX_MANIFEST_BYTES ? buffer.ToArray() : throw new InvalidDataException($"The body exceeds {MAX_MANIFEST_BYTES} bytes.");
    }

    /// <summary>
    /// Handles <c>GET /patches/{id}/diff</c>.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The diff as text or 404.</returns>
    private static async Task<IResult> GetDiffAsync(Guid id, IPatchService patches, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.File(await patches.ReadDiffAsync(id, cancellationToken), "text/x-diff; charset=utf-8");
        }
        catch (SyncBridgeException ex)
        {
            return NotFound(localizer.Describe(ex));
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/{id}/resolve</c>.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The resolution result.</returns>
    private static async Task<IResult> ResolveAsync(Guid id, ResolvePatchRequest request, NodeConfig node, IPatchService patches, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return NotFound(localizer.Format("Api.NoPatchesToResolve", node.Role));
        }

        try
        {
            return Results.Ok(await patches.ResolveAsync(id, request.Policy, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(localizer.Describe(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/send</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="request">The optional body naming the files to send; without it every changed file is sent.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The send result.</returns>
    private static async Task<IResult> SendAsync(NodeConfig node, IPatchService patches, SendPatchRequest? request, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).SendsPatches)
        {
            return NotFound(localizer.Format("Api.NoSend", node.Role));
        }

        try
        {
            return Results.Ok(await patches.SendAsync(request?.Paths, cancellationToken));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /patches/baseline</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The snapshot.</returns>
    private static async Task<IResult> BaselineAsync(NodeConfig node, IPatchService patches, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!node.ParticipatesInPatchChannel)
        {
            return NotFound(localizer.Format("Api.NoBaseline", node.Role));
        }

        try
        {
            return Results.Ok(await patches.RebuildBaselineAsync(cancellationToken));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>GET /state</c>.
    /// </summary>
    /// <param name="patches">The patch service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The state of the node, or 400 with the reason when the working tree or the sync base cannot be read.</returns>
    private static async Task<IResult> GetStateAsync(IPatchService patches, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await patches.GetStateAsync(cancellationToken));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>GET /state/summary</c>: the status without walking the working tree again.
    /// </summary>
    /// <param name="patches">The patch service.</param>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The status, or the reason it cannot be told.</returns>
    private static async Task<IResult> GetStateSummaryAsync(IPatchService patches, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await patches.GetSummaryAsync(cancellationToken));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Tells whether an exception is a failure of the node the user can act on (a working tree that cannot be read, a sync
    /// base that cannot be built), which is answered with its message instead of a bare 500.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for such a failure.</returns>
    private static bool IsNodeFailure(Exception exception)
        => exception is SyncBridgeException or IOException or UnauthorizedAccessException;

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
