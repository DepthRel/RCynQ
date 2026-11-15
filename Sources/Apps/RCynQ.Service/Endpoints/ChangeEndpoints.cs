using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Partial;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Endpoints of the changed files of the working tree: the diff of a file against the version of the peer, the files for
/// an external tool, the choice of the changes that go, and taking back the changes of a file.
/// </summary>
public static class ChangeEndpoints
{
    /// <summary>
    /// Maps the endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapChangeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.CHANGES_DIFF, GetDiffAsync);
        app.MapPost(ApiRoutes.CHANGES_COMPARE, CompareAsync);
        app.MapPut(ApiRoutes.CHANGES_PARTIAL, ChooseAsync);
        app.MapDelete(ApiRoutes.CHANGES_PARTIAL, ResetAsync);
        app.MapPut(ApiRoutes.CHANGES_PARTIAL_CONTENT, SetContentAsync);
        app.MapPost(ApiRoutes.CHANGES_REVERT, RevertAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>GET /changes/diff?path=</c>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="node">The local node.</param>
    /// <param name="changes">The service of the changes.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The diff.</returns>
    private static Task<IResult> GetDiffAsync(string? path, NodeConfig node, PartialChangeService changes, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () => Results.Ok(await changes.GetDiffAsync(path ?? string.Empty, cancellationToken)));

    /// <summary>
    /// Handles <c>POST /changes/compare</c>.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="changes">The service of the changes.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The files for the tool.</returns>
    private static Task<IResult> CompareAsync(CompareFilesRequest request, NodeConfig node, PartialChangeService changes, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () => Results.Ok(await changes.PrepareCompareAsync(request, cancellationToken)));

    /// <summary>
    /// Handles <c>PUT /changes/partial</c>.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="changes">The service of the changes.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>What the choice came to.</returns>
    private static Task<IResult> ChooseAsync(ChooseChangesRequest request, NodeConfig node, PartialChangeService changes, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () => Results.Ok(await changes.ChooseAsync(request, cancellationToken)));

    /// <summary>
    /// Handles <c>PUT /changes/partial/content</c>.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="changes">The service of the changes.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>What the choice came to.</returns>
    private static Task<IResult> SetContentAsync(SetPartialContentRequest request, NodeConfig node, PartialChangeService changes, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () => Results.Ok(await changes.SetContentAsync(request, cancellationToken)));

    /// <summary>
    /// Handles <c>DELETE /changes/partial?path=</c>.
    /// </summary>
    /// <param name="path">The path of the file.</param>
    /// <param name="node">The local node.</param>
    /// <param name="changes">The service of the changes.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>No content.</returns>
    private static Task<IResult> ResetAsync(string? path, NodeConfig node, PartialChangeService changes, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () =>
        {
            await changes.ResetAsync(path ?? string.Empty, cancellationToken);
            return Results.NoContent();
        });

    /// <summary>
    /// Handles <c>POST /changes/revert</c>.
    /// </summary>
    /// <param name="request">The request body.</param>
    /// <param name="node">The local node.</param>
    /// <param name="revert">Takes back the changes of a file.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>Whether the file was restored, deleted or had nothing to take back.</returns>
    private static Task<IResult> RevertAsync(RevertChangesRequest request, NodeConfig node, ChangeRevertOperation revert, ILocalizer localizer, CancellationToken cancellationToken)
        => RunAsync(node, localizer, async () => Results.Ok(await revert.ExecuteAsync(request, cancellationToken)));

    /// <summary>
    /// Runs a request of a node with a working tree and words its failure: 400 for a request that cannot be served, 409
    /// for a choice made on files that changed since.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="work">The work of the request.</param>
    /// <returns>The answer.</returns>
    private static async Task<IResult> RunAsync(NodeConfig node, ILocalizer localizer, Func<Task<IResult>> work)
    {
        if (!node.ParticipatesInPatchChannel)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Patch.NoChannel", node.Role)));
        }

        try
        {
            return await work();
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
}
