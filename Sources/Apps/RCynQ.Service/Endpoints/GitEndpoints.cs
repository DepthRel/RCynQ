using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Git channel endpoints.
/// </summary>
public static class GitEndpoints
{
    /// <summary>
    /// Maps the Git endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapGitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.GIT_BRANCH, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
        {
            if (!RoleCapabilities.For(node.Role).ShowsBranchesTab)
            {
                return Results.NotFound(new ErrorResponse(localizer.Format("Node.NoWorkingTree", node.Role)));
            }

            try
            {
                return Results.Ok(await git.GetBranchAsync(ct));
            }
            catch (SyncBridgeException ex)
            {
                return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
            }
        });

        app.MapPost(ApiRoutes.GIT_FETCH, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.FetchAsync(ct))
                : Results.NotFound(new ErrorResponse(localizer.Format("Api.NoFetch", node.Role))));

        app.MapPost(ApiRoutes.GIT_PULL, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.PullAsync(ct))
                : Results.NotFound(new ErrorResponse(localizer.Format("Api.NoPull", node.Role))));

        app.MapPost(ApiRoutes.GIT_BUNDLE, BundleAsync);
        app.MapPost(ApiRoutes.GIT_BRANCHES, BranchesAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /git/branches</c> from a Workstation: lists every branch of the repository with its commit.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="request">The repository asked for.</param>
    /// <param name="bundles">The bundle service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The branches, or the rejection.</returns>
    private static async Task<IResult> BranchesAsync(HttpContext http, GitBranchListRequest request, GitBundleService bundles, ILocalizer localizer, CancellationToken cancellationToken)
    {
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NODE_ID].FirstOrDefault(), http.Request.Headers[ApiHeaders.LINK_TOKEN].FirstOrDefault());
        try
        {
            return Results.Ok(await bundles.ListBranchesAsync(context, request.Repository, cancellationToken));
        }
        catch (PatchRejectedException ex)
        {
            return Results.Json(new PatchRejectionResponse(ex.Reason, localizer.Describe(ex)), statusCode: PatchRejectionStatusCodes.For(ex.Reason));
        }
        catch (SyncBridgeException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /git/bundle</c> from a Workstation: streams a git bundle with what the Workstation does not have yet.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="request">The commits the Workstation already has.</param>
    /// <param name="bundles">The bundle service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The bundle, <c>204</c> when the Workstation has everything, or the rejection.</returns>
    private static async Task<IResult> BundleAsync(HttpContext http, GitBundleRequest request, GitBundleService bundles, ILocalizer localizer, CancellationToken cancellationToken)
    {
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NODE_ID].FirstOrDefault(), http.Request.Headers[ApiHeaders.LINK_TOKEN].FirstOrDefault());
        GitBundleFile? bundle;
        try
        {
            bundle = await bundles.CreateAsync(request.Have ?? Array.Empty<string>(), context, request.Repository, cancellationToken);
        }
        catch (PatchRejectedException ex)
        {
            return Results.Json(new PatchRejectionResponse(ex.Reason, localizer.Describe(ex)), statusCode: PatchRejectionStatusCodes.For(ex.Reason));
        }
        catch (SyncBridgeException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }

        if (bundle is null)
        {
            return Results.NoContent();
        }

        if (bundle.Head is not null)
        {
            http.Response.Headers[ApiHeaders.GIT_HEAD] = bundle.Head;
        }

        // The file is deleted when the response is complete; until then it is streamed from disk, never held in memory.
        http.Response.OnCompleted(async () => await bundle.DisposeAsync());
        var stream = new FileStream(bundle.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Results.Stream(stream, "application/x-git-bundle");
    }
}
