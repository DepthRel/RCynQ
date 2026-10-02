using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Sync.GitChannel;
using RCynQ.Localization;

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
        app.MapGet(ApiRoutes.GitBranch, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
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

        app.MapPost(ApiRoutes.GitFetch, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.FetchAsync(ct))
                : Results.NotFound(new ErrorResponse(localizer.Format("Api.NoFetch", node.Role))));

        app.MapPost(ApiRoutes.GitPull, async (NodeConfig node, GitChannelService git, ILocalizer localizer, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.PullAsync(ct))
                : Results.NotFound(new ErrorResponse(localizer.Format("Api.NoPull", node.Role))));

        app.MapPost(ApiRoutes.GitBundle, BundleAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /git/bundle</c> from a Source: streams a git bundle with what the Source does not have yet.
    /// </summary>
    /// <param name="http">The HTTP context.</param>
    /// <param name="request">The commits the Source already has.</param>
    /// <param name="bundles">The bundle service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The bundle, <c>204</c> when the Source has everything, or the rejection.</returns>
    private static async Task<IResult> BundleAsync(HttpContext http, GitBundleRequest request, GitBundleService bundles, ILocalizer localizer, CancellationToken cancellationToken)
    {
        var context = new PatchReceiveContext(http.Request.Headers[ApiHeaders.NodeId].FirstOrDefault(), http.Request.Headers[ApiHeaders.LinkToken].FirstOrDefault());
        GitBundleFile? bundle;
        try
        {
            bundle = await bundles.CreateAsync(request.Have ?? Array.Empty<string>(), context, cancellationToken);
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
            http.Response.Headers[ApiHeaders.GitHead] = bundle.Head;
        }

        // The file is deleted when the response is complete; until then it is streamed from disk, never held in memory.
        http.Response.OnCompleted(async () => await bundle.DisposeAsync());
        var stream = new FileStream(bundle.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Results.Stream(stream, "application/x-git-bundle");
    }
}
