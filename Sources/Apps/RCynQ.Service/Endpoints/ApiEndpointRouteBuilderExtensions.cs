using Microsoft.AspNetCore.Routing;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Maps all RCynQ API endpoints.
/// </summary>
public static class ApiEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps health, state, patch, link, Git, diagnostics, configuration and log endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapRCynQApi(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app
            .MapHealthEndpoints()
            .MapServiceEndpoints()
            .MapPatchEndpoints()
            .MapMergeSessionEndpoints()
            .MapCommitEndpoints()
            .MapLinkEndpoints()
            .MapGitEndpoints()
            .MapDiagnosticsEndpoints()
            .MapConfigEndpoints()
            .MapEnrollmentEndpoints()
            .MapLogEndpoints()
            .MapDataEndpoints()
            .MapPeerEndpoints()
            .MapRepositoryEndpoints();
    }
}
