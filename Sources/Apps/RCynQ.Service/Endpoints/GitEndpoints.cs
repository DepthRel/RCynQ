using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
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
        app.MapGet(ApiRoutes.GitBranch, async (NodeConfig node, GitChannelService git, CancellationToken ct) =>
        {
            if (!RoleCapabilities.For(node.Role).ShowsBranchesTab)
            {
                return Results.NotFound(new ErrorResponse(Texts.Format("Node.NoWorkingTree", node.Role)));
            }

            try
            {
                return Results.Ok(await git.GetBranchAsync(ct));
            }
            catch (SyncBridgeException ex)
            {
                return Results.BadRequest(new ErrorResponse(ex.Message));
            }
        });

        app.MapPost(ApiRoutes.GitFetch, async (NodeConfig node, GitChannelService git, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.FetchAsync(ct))
                : Results.NotFound(new ErrorResponse(Texts.Format("Api.NoFetch", node.Role))));

        app.MapPost(ApiRoutes.GitPull, async (NodeConfig node, GitChannelService git, CancellationToken ct) =>
            RoleCapabilities.For(node.Role).FetchesGit
                ? Results.Ok(await git.PullAsync(ct))
                : Results.NotFound(new ErrorResponse(Texts.Format("Api.NoPull", node.Role))));
        return app;
    }
}
