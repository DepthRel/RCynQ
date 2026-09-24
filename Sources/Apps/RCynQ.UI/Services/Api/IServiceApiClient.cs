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

    /// <summary>Gets recent patches.</summary>
    /// <param name="take">The maximum number of patches.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The patches, newest first.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetPatchesAsync(int take, CancellationToken cancellationToken = default);

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

    /// <summary>Fetches branches from Dest.</summary>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default);

    /// <summary>Fetches from Dest and fast-forwards the current branch.</summary>
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
    /// <param name="host">The advertised host.</param>
    /// <param name="user">The advertised SSH user.</param>
    /// <param name="sshPort">The advertised SSH port.</param>
    /// <param name="sshPublicKey">The SSH public key line to include, or <see langword="null"/>.</param>
    /// <param name="hostKeyFingerprint">The sshd host key fingerprint to include, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The exported configuration.</returns>
    Task<ExportedNodeConfiguration> ExportConfigAsync(string? host, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default);

    /// <summary>Imports a peer configuration.</summary>
    /// <param name="configuration">The configuration.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The result.</returns>
    Task<ImportResult> ImportConfigAsync(ExportedNodeConfiguration configuration, CancellationToken cancellationToken = default);

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
}
