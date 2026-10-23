using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.UI.Services.Api;

/// <summary>
/// Typed client of the local RCynQ service API. All methods throw <see cref="ServiceApiException"/> for error answers
/// and <see cref="HttpRequestException"/> when the service is not reachable.
/// </summary>
public interface IServiceApiClient
{
    /// <summary>Gets the local node configuration.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The configuration.</returns>
    Task<NodeConfigurationDto> GetConfigAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the synchronization status.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The status.</returns>
    Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets the synchronization status as of the last look at the working tree; the service does not walk it again.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The status.</returns>
    Task<NodeSyncStatus> GetStateSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>Gets one page of the patch history.</summary>
    /// <param name="skip">The number of the newest patches to skip.</param>
    /// <param name="take">The maximum number of patches; the service gives at most <see cref="ApiLimits.MAX_PATCHES_PAGE"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The patches, newest first.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetPatchesAsync(int skip, int take, CancellationToken cancellationToken = default);

    /// <summary>Gets the unified diff of a patch.</summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The diff text.</returns>
    Task<string> GetPatchDiffAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>Sends the local changes.</summary>
    /// <param name="paths">The relative paths of the files to send, or <see langword="null"/> to send every changed file.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The send result.</returns>
    Task<SendPatchResult> SendPatchAsync(IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default);

    /// <summary>Creates the sync base from the working tree.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The snapshot.</returns>
    Task<SnapshotInfo> CreateBaselineAsync(CancellationToken cancellationToken = default);

    /// <summary>Resolves a conflicting patch.</summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<PatchApplyResult> ResolvePatchAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>Gets the configured links.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The links.</returns>
    Task<IReadOnlyList<LinkDto>> GetLinksAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds or updates a link.</summary>
    /// <param name="link">The link settings.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The stored link.</returns>
    Task<LinkDto> SaveLinkAsync(ChainLinkOptions link, CancellationToken cancellationToken = default);

    /// <summary>Deletes a link.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes when the link is deleted.</returns>
    Task DeleteLinkAsync(Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>Probes all links.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The states.</returns>
    Task<IReadOnlyList<LinkState>> GetLinkStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>Starts a link.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The resulting state.</returns>
    Task<LinkState> StartLinkAsync(Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>Stops a link.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The resulting state.</returns>
    Task<LinkState> StopLinkAsync(Guid linkId, CancellationToken cancellationToken = default);

    /// <summary>Gets the branch state.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The branch information.</returns>
    Task<GitBranchInfo> GetBranchAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches branches from Origin.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches from Origin and fast-forwards the current branch.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default);

    /// <summary>Runs diagnostics.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The results.</returns>
    Task<DiagnosticsEvent> RunDiagnosticsAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies a confirmed safe fix.</summary>
    /// <param name="fix">The fix.</param>
    /// <param name="confirmed">Whether the user confirmed the fix.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The outcome.</returns>
    Task<AutoFixResult> ApplyFixAsync(DiagnosticFix fix, bool confirmed, CancellationToken cancellationToken = default);

    /// <summary>Checks whether the service answers <c>/health</c>; never throws for connection failures.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns><see langword="true"/> when the service answers.</returns>
    Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default);

    /// <summary>Asks the service to stop gracefully.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes when the request was accepted.</returns>
    Task ShutdownServiceAsync(CancellationToken cancellationToken = default);

    /// <summary>Removes the finished data of the node whatever its age: patches, history, diagnostics runs, backups.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>What was removed.</returns>
    Task<DataCleanupReport> CleanDataAsync(CancellationToken cancellationToken = default);

    /// <summary>Probes the network and SSH environment for an export.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The report.</returns>
    Task<NodeEnvironmentReport> ProbeExportAsync(CancellationToken cancellationToken = default);

    /// <summary>Allows the imported neighbours in the SSH server settings of this computer.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result; <see cref="PeerAccessResult.RequiresElevation"/> when administrator rights are needed.</returns>
    Task<PeerAccessResult> AllowPeerAccessAsync(CancellationToken cancellationToken = default);

    /// <summary>Generates the node SSH key when none exists and probes again.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The report.</returns>
    Task<NodeEnvironmentReport> GenerateSshKeyAsync(CancellationToken cancellationToken = default);

    /// <summary>Exports the public node configuration.</summary>
    /// <param name="addresses">The advertised addresses, the preferred one first.</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="sshPort">The advertised SSH port.</param>
    /// <param name="sshPublicKey">The SSH public key line to include, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint to include, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The exported configuration.</returns>
    Task<ExportedNodeConfiguration> ExportConfigAsync(IReadOnlyList<string> addresses, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default);

    /// <summary>Imports a peer configuration.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="decision">
    /// What to do when a peer of the imported role is known already, or <see langword="null"/> to be told the decisions
    /// to choose from; an import that needs none ignores it.
    /// </param>
    /// <param name="linkId">The link whose peer the imported node replaces, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result; when it asks for a decision nothing was imported.</returns>
    Task<ImportResult> ImportConfigAsync(ExportedNodeConfiguration configuration, ImportDecision? decision = null, Guid? linkId = null, CancellationToken cancellationToken = default);

    /// <summary>Creates an invitation for a neighbour.</summary>
    /// <param name="request">The role of the invited node and the lifetime.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The invitation.</returns>
    Task<CreatedInvitation> CreateInvitationAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Lists the invitations of the node, the newest first.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The invitations.</returns>
    Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken cancellationToken = default);

    /// <summary>Revokes an open invitation.</summary>
    /// <param name="id">The identifier of the invitation.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task representing the request.</returns>
    Task RevokeInvitationAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Joins the node that wrote an invitation.</summary>
    /// <param name="invitation">The invitation text.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>How every stage ended.</returns>
    Task<JoinResult> JoinAsync(string invitation, CancellationToken cancellationToken = default);

    /// <summary>Gets the routes of the links that have any, with the outcome of their last check.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The routes per link.</returns>
    Task<IReadOnlyList<LinkRoutesDto>> GetLinkRoutesAsync(CancellationToken cancellationToken = default);

    /// <summary>Pins a route of a link by hand, or hands the choice back to the node.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="routeId">The route to use, or <see langword="null"/> for the automatic choice.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The routes of the link.</returns>
    Task<LinkRoutesDto> SelectLinkRouteAsync(Guid linkId, Guid? routeId, CancellationToken cancellationToken = default);

    /// <summary>Changes the preference of a route.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="routeId">The route identifier.</param>
    /// <param name="priority">The preference; lower values are preferred.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The routes of the link.</returns>
    Task<LinkRoutesDto> SetLinkRoutePriorityAsync(Guid linkId, Guid routeId, int priority, CancellationToken cancellationToken = default);

    /// <summary>Removes a route of a link.</summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="routeId">The route identifier.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The routes of the link.</returns>
    Task<LinkRoutesDto> RemoveLinkRouteAsync(Guid linkId, Guid routeId, CancellationToken cancellationToken = default);

    /// <summary>Changes the role in the configuration file.</summary>
    /// <param name="request">The request.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<RoleChangeResult> SetRoleAsync(SetRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>Gets recent log entries.</summary>
    /// <param name="level">The minimum level name.</param>
    /// <param name="take">The maximum number of entries.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The entries, newest first.</returns>
    Task<IReadOnlyList<LogEntryDto>> GetLogsAsync(string level, int take, CancellationToken cancellationToken = default);

    /// <summary>Gets the repositories of the node.</summary>
    /// <param name="askOrigin">Whether a Workstation asks the Origin for what it offers; that needs the link to it.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The repositories.</returns>
    Task<RepositoryListDto> GetRepositoriesAsync(bool askOrigin = true, CancellationToken cancellationToken = default);

    /// <summary>Adds a repository to the node.</summary>
    /// <param name="name">The name the nodes call the repository by.</param>
    /// <param name="path">The local folder.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The repositories of the node afterwards.</returns>
    Task<RepositoryListDto> AddRepositoryAsync(string name, string path, CancellationToken cancellationToken = default);

    /// <summary>Removes a repository from the node; its files stay.</summary>
    /// <param name="name">The repository name.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The repositories of the node afterwards.</returns>
    Task<RepositoryListDto> RemoveRepositoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Makes a repository the one the node works with.</summary>
    /// <param name="name">The repository name.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The repositories of the node afterwards.</returns>
    Task<RepositoryListDto> ActivateRepositoryAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Gets the commits of the current branch the Origin does not have yet (Workstation).</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The commits, oldest first, or why there are none to send.</returns>
    Task<UnsentCommitsResponse> GetUnsentCommitsAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends unsent commits to the Origin (Workstation).</summary>
    /// <param name="upTo">The last commit to send, or <see langword="null"/> for every unsent commit.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The send result.</returns>
    Task<SendPatchResult> SendCommitsAsync(string? upTo, CancellationToken cancellationToken = default);

    /// <summary>Rolls back unsent commits (Workstation).</summary>
    /// <param name="request">The commit to roll back to, the mode and the commit the user saw checked out.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The outcome; a refusal is reported in it.</returns>
    Task<GitOperationResult> ResetCommitsAsync(ResetCommitsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Applies a delivery of commits that waits (Origin).</summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>Applied, or still waiting with the reason.</returns>
    Task<PatchApplyResult> ApplyWaitingCommitsAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>Declines a delivery of commits that waits (Origin).</summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<PatchApplyResult> DeclineWaitingCommitsAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>Switches whether this Origin takes commits from the Workstation.</summary>
    /// <param name="enabled">Whether commits are taken.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>A task that completes when the setting is saved.</returns>
    Task SetAcceptCommitsAsync(bool enabled, CancellationToken cancellationToken = default);
}
