using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Sync.Repositories;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// The local API of the repositories of the node: listing, adding, removing and switching.
/// </summary>
/// <remarks>
/// These routes need the local API token. What the neighbour may ask is served by <see cref="PeerEndpoints"/>.
/// </remarks>
public static class RepositoryEndpoints
{
    /// <summary>
    /// Maps the repository endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapRepositoryEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.REPOSITORIES, async (RepositoryService repositories, bool? origin, CancellationToken cancellationToken) =>
            Results.Ok(await repositories.ListAsync(origin ?? true, cancellationToken)));
        app.MapPost(ApiRoutes.REPOSITORIES, (AddRepositoryRequest request, RepositoryService repositories, ILocalizer localizer, CancellationToken cancellationToken) =>
            RunAsync(localizer, async () =>
            {
                await repositories.AddAsync(request.Name, request.Path, cancellationToken);
                return await repositories.ListAsync(false, cancellationToken);
            }));
        app.MapDelete(ApiRoutes.REPOSITORY_BY_NAME, (string name, RepositoryService repositories, ILocalizer localizer, CancellationToken cancellationToken) =>
            RunAsync(localizer, async () =>
            {
                await repositories.RemoveAsync(name, cancellationToken);
                return await repositories.ListAsync(false, cancellationToken);
            }));
        app.MapPost(ApiRoutes.REPOSITORY_ACTIVATE, (string name, RepositoryService repositories, ILocalizer localizer, CancellationToken cancellationToken) =>
            RunAsync(localizer, async () =>
            {
                await repositories.ActivateAsync(name, cancellationToken);
                return await repositories.ListAsync(false, cancellationToken);
            }));
        return app;
    }

    /// <summary>
    /// Runs an operation and turns a refusal into a <c>400</c> answer with its reason.
    /// </summary>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="operation">The operation; it returns the repositories as they are afterwards.</param>
    /// <returns>The repositories, or the reason of the refusal.</returns>
    private static async Task<IResult> RunAsync(ILocalizer localizer, Func<Task<RepositoryListDto>> operation)
    {
        try
        {
            return Results.Ok(await operation());
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }
}
