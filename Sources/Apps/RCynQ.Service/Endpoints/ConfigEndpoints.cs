using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;
using RCynQ.Service.Hosting;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Access;
using RCynQ.Sync.Configuration.Probing;

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
        app.MapGet(ApiRoutes.CONFIG, (NodeConfig node, IActiveRepository repository, SyncBridgeOptions options, ServiceRuntimeInfo runtime) =>
        {
            // The paths are those of the repository the node works with now.
            var current = node.WithRepository(repository.Current);
            return Results.Ok(new NodeConfigurationDto(node.Id, node.Name, node.Role, current.RepoPath, current.WorkdirPath, options.ListenPort, RoleCapabilities.For(node.Role), runtime.ConfigFilePath));
        });
        app.MapGet(ApiRoutes.CONFIG_EXPORT_PROBE, async (INodeEnvironmentProbe probe, CancellationToken cancellationToken) =>
            Results.Ok(await probe.ProbeAsync(cancellationToken)));
        app.MapPost(ApiRoutes.CONFIG_SSH_KEY, GenerateSshKeyAsync);
        app.MapPost(ApiRoutes.SSH_AUTHORIZED_KEYS, AllowPeerAccessAsync);
        app.MapGet(ApiRoutes.CONFIG_EXPORT, ExportAsync);
        app.MapPost(ApiRoutes.CONFIG_IMPORT, ImportAsync);
        app.MapPut(ApiRoutes.CONFIG_ROLE, SetRoleAsync);
        return app;
    }

    /// <summary>
    /// Handles <c>POST /config/ssh-key</c>.
    /// </summary>
    /// <param name="probe">The environment probe.</param>
    /// <param name="runtime">The runtime information.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The environment report after the key exists.</returns>
    private static async Task<IResult> GenerateSshKeyAsync(INodeEnvironmentProbe probe, ServiceRuntimeInfo runtime, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await probe.GenerateSshKeyAsync(runtime.ConfigFilePath, cancellationToken));
        }
        catch (Exception ex) when (ex is SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /ssh/authorized-keys</c>.
    /// </summary>
    /// <param name="access">The neighbour access service.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The access result.</returns>
    private static async Task<IResult> AllowPeerAccessAsync(PeerAccessService access, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await access.ApplyAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>GET /config/export</c>.
    /// </summary>
    /// <param name="transfer">The transfer service.</param>
    /// <param name="address">The advertised addresses, the preferred one first (the query repeats <c>address</c>).</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="port">The advertised SSH port.</param>
    /// <param name="sshPublicKey">The SSH public key line.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The exported configuration.</returns>
    private static async Task<IResult> ExportAsync(NodeConfigurationTransfer transfer, string[]? address, string? user, int? port, string? sshPublicKey, string? hostKeyFingerprint, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await transfer.ExportAsync(address ?? Array.Empty<string>(), user, port ?? 22, sshPublicKey, hostKeyFingerprint, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>POST /config/import</c>.
    /// </summary>
    /// <param name="configuration">The peer configuration.</param>
    /// <param name="transfer">The transfer service.</param>
    /// <param name="decision">
    /// What to do when a peer of the imported role is known already; without it such an import changes nothing and the
    /// answer names the decisions to choose from.
    /// </param>
    /// <param name="link">The link whose peer the imported node replaces, or <see langword="null"/>.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The import result.</returns>
    private static async Task<IResult> ImportAsync(ExportedNodeConfiguration configuration, NodeConfigurationTransfer transfer, ImportDecision? decision, Guid? link, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            return Results.Ok(await transfer.ImportAsync(configuration, decision, link, cancellationToken));
        }
        catch (FormatException ex)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }

    /// <summary>
    /// Handles <c>PUT /config/role</c>.
    /// </summary>
    /// <param name="request">The request.</param>
    /// <param name="startNode">The running node.</param>
    /// <param name="repository">Tells which repository the node works with now.</param>
    /// <param name="runtime">The runtime information.</param>
    /// <param name="localizer">The localizer that words the answer for the client.</param>
    /// <param name="cancellationToken">The request token.</param>
    /// <returns>The role change result.</returns>
    private static async Task<IResult> SetRoleAsync(SetRoleRequest request, NodeConfig startNode, IActiveRepository repository, ServiceRuntimeInfo runtime, ILocalizer localizer, CancellationToken cancellationToken)
    {
        try
        {
            var node = startNode.WithRepository(repository.Current);
            var repo = request.Role == Core.Enums.NodeRole.Relay ? null : request.RepoPath ?? node.RepoPath;
            var workdir = request.Role == Core.Enums.NodeRole.Relay ? null : request.WorkdirPath ?? node.WorkdirPath;
            _ = new NodeConfig(node.Id, node.Name, request.Role, repo, workdir);
            await ConfigurationFileEditor.SetRoleAsync(runtime.ConfigFilePath, request.Role, request.RepoPath, request.WorkdirPath, cancellationToken);
            var restart = request.Role != node.Role || repo != node.RepoPath || workdir != node.WorkdirPath;
            return Results.Ok(new RoleChangeResult(true, restart, restart
                ? localizer.Format("Api.RoleSaved", runtime.ConfigFilePath, request.Role)
                : localizer.Get("Api.ConfigUnchanged")));
        }
        catch (Exception ex) when (ex is ArgumentException or SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            return Results.BadRequest(new ErrorResponse(localizer.Describe(ex)));
        }
    }
}
