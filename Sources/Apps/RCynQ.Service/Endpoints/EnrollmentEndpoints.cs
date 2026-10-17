using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Api;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Service.Hosting;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Maps the endpoints of invitations: creating, listing and revoking them on the local API, joining with one, and the
/// public enrollment of an invited node.
/// </summary>
public static class EnrollmentEndpoints
{
    /// <summary>
    /// Maps the endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost(ApiRoutes.PEER_ENROLL, EnrollAsync);
        app.MapGet(ApiRoutes.CONFIG_INVITATIONS, (InvitationService invitations, CancellationToken cancellationToken) => invitations.ListAsync(cancellationToken));
        app.MapPost(ApiRoutes.CONFIG_INVITATIONS, CreateAsync);
        app.MapDelete(ApiRoutes.CONFIG_INVITATION_BY_ID, async (Guid id, InvitationService invitations, CancellationToken cancellationToken) =>
            await invitations.RevokeAsync(id, cancellationToken) ? Results.NoContent() : Results.NotFound());
        app.MapPost(ApiRoutes.CONFIG_JOIN, JoinAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /peer/enroll</c>: takes an invited node. A request that cannot be taken is answered with a refusal,
    /// not an error, so the node that enrolls always learns why.
    /// </summary>
    /// <param name="request">The enrollment.</param>
    /// <param name="enrollment">The enrollment service.</param>
    /// <param name="localizer">The localizer that words the answer for the enrolling node.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The answer.</returns>
    private static async Task<IResult> EnrollAsync(EnrollmentRequest? request, EnrollmentService enrollment, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (request?.Card is null || string.IsNullOrEmpty(request.Secret))
        {
            return Results.Ok(new EnrollmentResponse(false, localizer.Get("Enrollment.TicketRefused")));
        }

        try
        {
            return Results.Ok(await enrollment.EnrollAsync(request, cancellationToken));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            return Results.Ok(new EnrollmentResponse(false, localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /config/invitations</c>.
    /// </summary>
    /// <param name="request">The role of the invited node and the lifetime.</param>
    /// <param name="invitations">The invitation service.</param>
    /// <param name="runtime">The runtime information with the configuration file.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The invitation.</returns>
    private static async Task<IResult> CreateAsync(CreateInvitationRequest request, InvitationService invitations, ServiceRuntimeInfo runtime, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await invitations.CreateAsync(request, runtime.ConfigFilePath, cancellationToken));
        }
        catch (Exception ex) when (ex is ArgumentException or SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /config/join</c>; how every stage ended is in the answer, and every stage is published to the
    /// application while it runs.
    /// </summary>
    /// <param name="request">The invitation.</param>
    /// <param name="join">The join service.</param>
    /// <param name="runtime">The runtime information with the configuration file.</param>
    /// <param name="events">The publisher of the stages.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The result.</returns>
    private static async Task<IResult> JoinAsync(JoinRequest request, JoinService join, ServiceRuntimeInfo runtime, IEventPublisher events, CancellationToken cancellationToken)
        => Results.Ok(await join.JoinAsync(request.Invitation, runtime.ConfigFilePath, progress => events.PublishJoinProgressAsync(progress, cancellationToken), cancellationToken));
}
