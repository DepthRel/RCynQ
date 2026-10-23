using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Service.Hosting;
using RCynQ.Sync.Commits;
using RCynQ.Sync.Configuration;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Endpoints of the delivery of commits: the unsent commits, sending and rolling them back on a Workstation; applying or
/// declining the commits that wait, and whether commits are taken at all, on an Origin.
/// </summary>
public static class CommitEndpoints
{
    /// <summary>
    /// The logger category.
    /// </summary>
    private const string LOGGER_CATEGORY = "RCynQ.Service.Commits";

    /// <summary>
    /// Maps the endpoints of the delivery of commits.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapCommitEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.COMMITS, GetUnsentAsync);
        app.MapPost(ApiRoutes.COMMITS_SEND, SendAsync);
        app.MapPost(ApiRoutes.COMMITS_RESET, ResetAsync);
        app.MapPost(ApiRoutes.APPLY_PATCH, ApplyAsync);
        app.MapPost(ApiRoutes.DECLINE_PATCH, DeclineAsync);
        app.MapPut(ApiRoutes.CONFIG_ACCEPT_COMMITS, SetAcceptanceAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>GET /commits</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="commits">The commit service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The unsent commits, or 404 for a node that sends none.</returns>
    private static async Task<IResult> GetUnsentAsync(NodeConfig node, ICommitService commits, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).SendsCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyWorkstation", node.Role)));
        }

        try
        {
            return Results.Ok(await commits.GetUnsentAsync(cancellationToken));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /commits/send</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="commits">The commit service.</param>
    /// <param name="request">The optional body naming the last commit to send; without it every unsent commit is sent.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The send result.</returns>
    private static async Task<IResult> SendAsync(NodeConfig node, ICommitService commits, SendCommitsRequest? request, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).SendsCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyWorkstation", node.Role)));
        }

        try
        {
            var result = await commits.SendAsync(request?.UpTo, cancellationToken);
            loggers.CreateLogger(LOGGER_CATEGORY).LogInformation("Sent commits as delivery {PatchId}: {Status}", result.PatchId, result.Result?.Status);
            return Results.Ok(result);
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /commits/reset</c>.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="commits">The commit service.</param>
    /// <param name="request">The commit to roll back to, the mode and the commit the user saw checked out.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The outcome; a refusal is reported in it.</returns>
    private static async Task<IResult> ResetAsync(NodeConfig node, ICommitService commits, ResetCommitsRequest request, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).SendsCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyWorkstation", node.Role)));
        }

        return Results.Ok(await commits.ResetAsync(request.Target, request.Mode, request.ExpectedHead, cancellationToken));
    }

    /// <summary>
    /// Handles <c>POST /patches/{id}/apply</c>.
    /// </summary>
    /// <param name="id">The delivery.</param>
    /// <param name="node">The local node.</param>
    /// <param name="commits">The commit service.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>Applied, or still waiting with the reason.</returns>
    private static async Task<IResult> ApplyAsync(Guid id, NodeConfig node, ICommitService commits, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyOrigin", node.Role)));
        }

        return await DecideAsync(async () =>
        {
            var result = await commits.ApplyWaitingAsync(id, cancellationToken);
            loggers.CreateLogger(LOGGER_CATEGORY).LogInformation("Applying waiting commits {PatchId} by hand: {Status}", id, result.Status);
            return result;
        }, localizer);
    }

    /// <summary>
    /// Handles <c>POST /patches/{id}/decline</c>.
    /// </summary>
    /// <param name="id">The delivery.</param>
    /// <param name="node">The local node.</param>
    /// <param name="commits">The commit service.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The result.</returns>
    private static async Task<IResult> DeclineAsync(Guid id, NodeConfig node, ICommitService commits, ILoggerFactory loggers, ILocalizer localizer, CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyOrigin", node.Role)));
        }

        return await DecideAsync(async () =>
        {
            var result = await commits.DeclineWaitingAsync(id, cancellationToken);
            loggers.CreateLogger(LOGGER_CATEGORY).LogInformation("Declined waiting commits {PatchId}", id);
            return result;
        }, localizer);
    }

    /// <summary>
    /// Handles <c>PUT /config/accept-commits</c>: switches whether this Origin takes commits, now and in the configuration file.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="node">The local node.</param>
    /// <param name="acceptance">Tells whether commits are taken.</param>
    /// <param name="options">The node options.</param>
    /// <param name="runtime">The runtime information.</param>
    /// <param name="loggers">The logger factory.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>204, or the reason the setting could not be saved.</returns>
    private static async Task<IResult> SetAcceptanceAsync(
        AcceptCommitsRequest request,
        NodeConfig node,
        CommitAcceptance acceptance,
        SyncBridgeOptions options,
        ServiceRuntimeInfo runtime,
        ILoggerFactory loggers,
        ILocalizer localizer,
        CancellationToken cancellationToken)
    {
        if (!RoleCapabilities.For(node.Role).ReceivesCommits)
        {
            return Results.NotFound(new ErrorResponse(localizer.Format("Commits.OnlyOrigin", node.Role)));
        }

        try
        {
            if (runtime.ConfigFilePath is { } path)
            {
                await ConfigurationFileEditor.UpdateAsync(
                    path,
                    section => ConfigurationFileEditor.SetProperty(section, nameof(SyncBridgeOptions.AcceptCommits), request.Enabled),
                    cancellationToken);
            }

            options.AcceptCommits = request.Enabled;
            acceptance.IsEnabled = request.Enabled;
            loggers.CreateLogger(LOGGER_CATEGORY).LogWarning("Taking commits from the Workstation is now {State}", request.Enabled ? "on" : "off");
            return Results.NoContent();
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Runs a decision on commits that wait and maps its failures to answers.
    /// </summary>
    /// <param name="decision">The decision.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <returns>The result, 404 for an unknown delivery or 409 for one that does not wait.</returns>
    private static async Task<IResult> DecideAsync(Func<Task<Core.Patches.PatchApplyResult>> decision, ILocalizer localizer)
    {
        try
        {
            return Results.Ok(await decision());
        }
        catch (KeyNotFoundException ex)
        {
            return Results.NotFound(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (InvalidOperationException ex)
        {
            return Results.Conflict(new ErrorResponse(localizer.Describe(ex)));
        }
        catch (Exception ex) when (IsNodeFailure(ex))
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Tells whether an exception is a failure of the node the user can act on.
    /// </summary>
    /// <param name="exception">The exception.</param>
    /// <returns><see langword="true"/> for such a failure.</returns>
    private static bool IsNodeFailure(Exception exception)
        => exception is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException;
}
