using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Service.Hosting;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Configuration.Probing;
using RCynQ.Localization;

namespace RCynQ.Service.Endpoints;

/// <summary>
/// Configuration endpoints: node information, environment probe, SSH key, export, import and role change.
/// </summary>
public static class ConfigEndpoints
{
    /// <summary>
    /// Maps the configuration endpoints.
    /// </summary>
    /// <param name="app">The route builder.</param>
    /// <returns>The same route builder.</returns>
    public static IEndpointRouteBuilder MapConfigEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet(ApiRoutes.Config, (NodeConfig node, SyncBridgeOptions options, ServiceRuntimeInfo runtime) =>
            Results.Ok(new NodeConfigurationDto(node.Id, node.Name, node.Role, node.RepoPath, node.WorkdirPath, options.ListenPort, RoleCapabilities.For(node.Role), runtime.ConfigFilePath)));
        app.MapGet(ApiRoutes.ConfigExportProbe, async (INodeEnvironmentProbe probe, CancellationToken cancellationToken) =>
            Results.Ok(await probe.ProbeAsync(cancellationToken)));
        app.MapPost(ApiRoutes.ConfigSshKey, GenerateSshKeyAsync);
        app.MapPost(ApiRoutes.SshAuthorizedKeys, AllowPeerAccessAsync);
        app.MapGet(ApiRoutes.ConfigExport, ExportAsync);
        app.MapPost(ApiRoutes.ConfigImport, ImportAsync);
        app.MapPut(ApiRoutes.ConfigRole, SetRoleAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /config/ssh-key</c>.
    /// </summary>
    /// <param name="probe">The environment probe.</param>
    /// <param name="runtime">The runtime information.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The environment report after the key exists.</returns>
    private static async Task<IResult> GenerateSshKeyAsync(INodeEnvironmentProbe probe, ServiceRuntimeInfo runtime, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await probe.GenerateSshKeyAsync(runtime.ConfigFilePath, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>POST /ssh/authorized-keys</c>.
    /// </summary>
    /// <param name="access">The neighbour access service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The access result.</returns>
    private static async Task<IResult> AllowPeerAccessAsync(PeerAccessService access, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await access.ApplyAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>GET /config/export</c>.
    /// </summary>
    /// <param name="transfer">The transfer service.</param>
    /// <param name="host">The advertised host.</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="port">The advertised SSH port.</param>
    /// <param name="sshPublicKey">The SSH public key line.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The exported configuration.</returns>
    private static async Task<IResult> ExportAsync(NodeConfigurationTransfer transfer, string? host, string? user, int? port, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await transfer.ExportAsync(host, user, port ?? 22, sshPublicKey, hostKeyFingerprint, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>POST /config/import</c>.
    /// </summary>
    /// <param name="configuration">The peer configuration.</param>
    /// <param name="transfer">The transfer service.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The import result.</returns>
    private static async Task<IResult> ImportAsync(ExportedNodeConfiguration configuration, NodeConfigurationTransfer transfer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await transfer.ImportAsync(configuration, cancellationToken));
        }
        catch (FormatException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    /// <summary>
    /// Handles <c>PUT /config/role</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="node">The running node.</param>
    /// <param name="runtime">The runtime information.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The role change result.</returns>
    private static async Task<IResult> SetRoleAsync(SetRoleRequest request, NodeConfig node, ServiceRuntimeInfo runtime, CancellationToken cancellationToken)
    {
        try
        {
            var repo = request.Role == Core.Enums.NodeRole.Mediator ? null : request.RepoPath ?? node.RepoPath;
            var workdir = request.Role == Core.Enums.NodeRole.Mediator ? null : request.WorkdirPath ?? node.WorkdirPath;
            _ = new NodeConfig(node.Id, node.Name, request.Role, repo, workdir);
            await ConfigurationFileEditor.SetRoleAsync(runtime.ConfigFilePath, request.Role, request.RepoPath, request.WorkdirPath, cancellationToken);
            var restart = request.Role != node.Role || repo != node.RepoPath || workdir != node.WorkdirPath;
            return Results.Ok(new RoleChangeResult(true, restart, restart
                ? Texts.Format("Api.RoleSaved", runtime.ConfigFilePath, request.Role)
                : Texts.Get("Api.ConfigUnchanged")));
        }
        catch (Exception ex) when (ex is ArgumentException or SyncBridgeException)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }
}
