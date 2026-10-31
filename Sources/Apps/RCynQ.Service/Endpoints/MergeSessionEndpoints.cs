using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Services;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Merge session endpoints: the files of a conflicting patch the user merges in a merge tool, applying the merge and
/// giving it up.
/// </summary>
public static class MergeSessionEndpoints
{
    /// <summary>
    /// Maps the merge session endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapMergeSessionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.PATCH_MERGE_SESSIONS, PrepareAsync);
        app.MapPost(ApiRoutes.MERGE_SESSION_COMPLETE, CompleteAsync);
        app.MapDelete(ApiRoutes.MERGE_SESSION, Cancel);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /patches/{id}/merge-sessions</c>.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="node">The local node.</param>
    /// <param name="merges">The merge session operation.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The session, or the files no tool can merge.</returns>
    private static async Task<IResult> PrepareAsync(Guid id, NodeConfig node, MergeSessionOperation merges, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return NotFound(localizer.Format("Api.NoPatchesToResolve", node.Role));
        }

        try
        {
            return Results.Ok(await merges.PrepareAsync(id, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(localizer.Describe(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /merge-sessions/{id}/complete</c>.
    /// </summary>
    /// <param name="id">The session identifier.</param>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="merges">The merge session operation.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The resolution result; 404 for an unknown session, 409 when the merge no longer fits the working tree.</returns>
    private static async Task<IResult> CompleteAsync(Guid id, CompleteMergeSessionRequest request, NodeConfig node, MergeSessionOperation merges, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesPatches)
        {
            return NotFound(localizer.Format("Api.NoPatchesToResolve", node.Role));
        }

        try
        {
            return Results.Ok(await merges.CompleteAsync(id, request.ToolName, cancellationToken));
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(localizer.Describe(ex));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>DELETE /merge-sessions/{id}</c>: the merge is given up and the patch keeps waiting for a decision. An
    /// unknown session is gone already, which is what the client wants.
    /// </summary>
    /// <param name="id">The session identifier.</param>
    /// <param name="merges">The merge session operation.</param>
    /// <returns>An empty success result.</returns>
    private static IResult Cancel(Guid id, MergeSessionOperation merges)
    {
        merges.Cancel(id);
        return Results.NoContent();
    }

    /// <summary>
    /// Creates a 404 response.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <returns>The result.</returns>
    private static IResult NotFound(string message) => Results.NotFound(new ErrorResponse(message));
}
