using System.Net.Http.Json;
using System.Text.Json;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.UI.Services.Connection;

namespace RCynQ.UI.Services.Api;

/// <summary>
/// <see cref="IServiceApiClient"/> over HTTP with the local API token.
/// </summary>
public sealed class ServiceApiClient : IServiceApiClient, IDisposable
{
    /// <summary>
    /// JSON options matching the service.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    /// <summary>The HTTP client for ordinary requests; a request that takes longer than ten minutes is treated as a hang.</summary>
    private readonly HttpClient _http;

    /// <summary>
    /// The HTTP client for operations that legitimately take long: sending a patch, fetching branches, creating the baseline.
    /// It has no time limit; the caller cancels it.
    /// </summary>
    private readonly HttpClient _long;

    /// <summary>
    /// The localizer of the messages.
    /// </summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceApiClient"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="settings">The connection settings.</param>
    /// <param name="tokens">The token provider, or <see langword="null"/> to create one from <paramref name="settings"/>.</param>
    /// <param name="handler">The HTTP handler, or <see langword="null"/> for a handler that bypasses the system proxy.</param>
    public ServiceApiClient(ILocalizer localizer, ServiceConnectionSettings settings, ApiTokenProvider? tokens = null, HttpMessageHandler? handler = null)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentNullException.ThrowIfNull(settings);
        var tokenHandler = new ApiTokenHandler(_localizer, tokens ?? new ApiTokenProvider(settings), handler ?? new SocketsHttpHandler { UseProxy = false });
        _http = new HttpClient(tokenHandler)
        {
            BaseAddress = settings.ServiceUri,
            Timeout = TimeSpan.FromMinutes(10)
        };
        _long = new HttpClient(tokenHandler, disposeHandler: false)
        {
            BaseAddress = settings.ServiceUri,
            Timeout = Timeout.InfiniteTimeSpan
        };
    }

    /// <inheritdoc />
    public Task<NodeConfigurationDto> GetConfigAsync(CancellationToken cancellationToken = default) => GetAsync<NodeConfigurationDto>(ApiRoutes.CONFIG, cancellationToken);

    /// <inheritdoc />
    public Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default) => GetAsync<NodeSyncStatus>(ApiRoutes.STATE, cancellationToken);

    /// <inheritdoc />
    public Task<NodeSyncStatus> GetStateSummaryAsync(CancellationToken cancellationToken = default) => GetAsync<NodeSyncStatus>(ApiRoutes.STATE_SUMMARY, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatchMetadata>> GetPatchesAsync(int take, CancellationToken cancellationToken = default)
        => await GetAsync<PatchMetadata[]>($"{ApiRoutes.PATCHES}?take={take}", cancellationToken);

    /// <inheritdoc />
    public async Task<string> GetPatchDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(ApiRoutes.PatchDiffPath(patchId), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendPatchResult> SendPatchAsync(IReadOnlyList<string>? paths = null, CancellationToken cancellationToken = default)
        => PostAsync<SendPatchResult>(ApiRoutes.SEND_PATCH, paths is null ? null : new SendPatchRequest(paths), cancellationToken, longRunning: true);

    /// <inheritdoc />
    public Task<SnapshotInfo> CreateBaselineAsync(CancellationToken cancellationToken = default) => PostAsync<SnapshotInfo>(ApiRoutes.BASELINE, null, cancellationToken, longRunning: true);

    /// <inheritdoc />
    public Task<PatchApplyResult> ResolvePatchAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
        => PostAsync<PatchApplyResult>(ApiRoutes.ResolvePatchPath(patchId), new ResolvePatchRequest(policy), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkDto>> GetLinksAsync(CancellationToken cancellationToken = default) => await GetAsync<LinkDto[]>(ApiRoutes.LINKS, cancellationToken);

    /// <inheritdoc />
    public Task<LinkDto> SaveLinkAsync(ChainLinkOptions link, CancellationToken cancellationToken = default) => PostAsync<LinkDto>(ApiRoutes.LINKS, link, cancellationToken);

    /// <inheritdoc />
    public Task<CreatedInvitation> CreateInvitationAsync(CreateInvitationRequest request, CancellationToken cancellationToken = default)
        => PostAsync<CreatedInvitation>(ApiRoutes.CONFIG_INVITATIONS, request, cancellationToken, longRunning: true);

    /// <inheritdoc />
    public async Task<IReadOnlyList<InvitationDto>> GetInvitationsAsync(CancellationToken cancellationToken = default)
        => await GetAsync<InvitationDto[]>(ApiRoutes.CONFIG_INVITATIONS, cancellationToken);

    /// <inheritdoc />
    public async Task RevokeInvitationAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(ApiRoutes.InvitationPath(id), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public Task<JoinResult> JoinAsync(string invitation, CancellationToken cancellationToken = default)
        => PostAsync<JoinResult>(ApiRoutes.CONFIG_JOIN, new JoinRequest(invitation), cancellationToken, longRunning: true);

    /// <inheritdoc />
    public async Task DeleteLinkAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(ApiRoutes.LinkPath(linkId), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkState>> GetLinkStatusAsync(CancellationToken cancellationToken = default) => await GetAsync<LinkState[]>(ApiRoutes.LINK_STATUS, cancellationToken);

    /// <inheritdoc />
    public Task<LinkState> StartLinkAsync(Guid linkId, CancellationToken cancellationToken = default) => PostAsync<LinkState>(ApiRoutes.LinkUpPath(linkId), null, cancellationToken);

    /// <inheritdoc />
    public Task<LinkState> StopLinkAsync(Guid linkId, CancellationToken cancellationToken = default) => PostAsync<LinkState>(ApiRoutes.LinkDownPath(linkId), null, cancellationToken);

    /// <inheritdoc />
    public Task<GitBranchInfo> GetBranchAsync(CancellationToken cancellationToken = default) => GetAsync<GitBranchInfo>(ApiRoutes.GIT_BRANCH, cancellationToken);

    /// <inheritdoc />
    public Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default) => PostAsync<GitOperationResult>(ApiRoutes.GIT_FETCH, null, cancellationToken, longRunning: true);

    /// <inheritdoc />
    public Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default) => PostAsync<GitOperationResult>(ApiRoutes.GIT_PULL, null, cancellationToken, longRunning: true);

    /// <inheritdoc />
    public Task<DiagnosticsEvent> RunDiagnosticsAsync(CancellationToken cancellationToken = default) => PostAsync<DiagnosticsEvent>(ApiRoutes.DIAGNOSTICS_RUN, null, cancellationToken);

    /// <inheritdoc />
    public Task<AutoFixResult> ApplyFixAsync(DiagnosticFix fix, bool confirmed, CancellationToken cancellationToken = default)
        => PostAsync<AutoFixResult>(ApiRoutes.DIAGNOSTICS_FIX, new ApplyFixRequest(fix, confirmed), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.GetAsync(ApiRoutes.HEALTH, timeout.Token).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task ShutdownServiceAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _http.PostAsync(ApiRoutes.SERVICE_SHUTDOWN, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<DataCleanupReport> CleanDataAsync(CancellationToken cancellationToken = default)
        => PostAsync<DataCleanupReport>(ApiRoutes.DATA_CLEANUP, null, cancellationToken, longRunning: true);

    /// <inheritdoc />
    public Task<NodeEnvironmentReport> ProbeExportAsync(CancellationToken cancellationToken = default)
        => GetAsync<NodeEnvironmentReport>(ApiRoutes.CONFIG_EXPORT_PROBE, cancellationToken);

    /// <inheritdoc />
    public Task<PeerAccessResult> AllowPeerAccessAsync(CancellationToken cancellationToken = default)
        => PostAsync<PeerAccessResult>(ApiRoutes.SSH_AUTHORIZED_KEYS, null, cancellationToken);

    /// <inheritdoc />
    public Task<NodeEnvironmentReport> GenerateSshKeyAsync(CancellationToken cancellationToken = default)
        => PostAsync<NodeEnvironmentReport>(ApiRoutes.CONFIG_SSH_KEY, null, cancellationToken);

    /// <inheritdoc />
    public Task<ExportedNodeConfiguration> ExportConfigAsync(IReadOnlyList<string> addresses, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var query = new List<string> { $"port={sshPort}" };
        query.AddRange(addresses.Where(address => !string.IsNullOrWhiteSpace(address)).Select(address => "address=" + Uri.EscapeDataString(address)));
        if (!string.IsNullOrWhiteSpace(sshPublicKey))
        {
            query.Add("sshPublicKey=" + Uri.EscapeDataString(sshPublicKey));
        }

        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            query.Add("hostKeyFingerprint=" + Uri.EscapeDataString(hostKeyFingerprint));
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            query.Add("user=" + Uri.EscapeDataString(user));
        }

        return GetAsync<ExportedNodeConfiguration>($"{ApiRoutes.CONFIG_EXPORT}?{string.Join('&', query)}", cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImportResult> ImportConfigAsync(ExportedNodeConfiguration configuration, ImportDecision? decision = null, Guid? linkId = null, CancellationToken cancellationToken = default)
        => PostAsync<ImportResult>(ApiRoutes.ConfigImportPath(decision, linkId), configuration, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkRoutesDto>> GetLinkRoutesAsync(CancellationToken cancellationToken = default)
        => await GetAsync<LinkRoutesDto[]>(ApiRoutes.LINK_ROUTES, cancellationToken);

    /// <inheritdoc />
    public async Task<LinkRoutesDto> SelectLinkRouteAsync(Guid linkId, Guid? routeId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PutAsJsonAsync(ApiRoutes.LinkRouteSelectionPath(linkId), new SelectRouteRequest(routeId), _json, cancellationToken);
        return await ReadAsync<LinkRoutesDto>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LinkRoutesDto> SetLinkRoutePriorityAsync(Guid linkId, Guid routeId, int priority, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PutAsJsonAsync(ApiRoutes.LinkRoutePriorityPath(linkId, routeId), new SetRoutePriorityRequest(priority), _json, cancellationToken);
        return await ReadAsync<LinkRoutesDto>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<LinkRoutesDto> RemoveLinkRouteAsync(Guid linkId, Guid routeId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(ApiRoutes.LinkRoutePath(linkId, routeId), cancellationToken);
        return await ReadAsync<LinkRoutesDto>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<RoleChangeResult> SetRoleAsync(SetRoleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PutAsJsonAsync(ApiRoutes.CONFIG_ROLE, request, _json, cancellationToken);
        return await ReadAsync<RoleChangeResult>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogEntryDto>> GetLogsAsync(string level, int take, CancellationToken cancellationToken = default)
        => await GetAsync<LogEntryDto[]>($"{ApiRoutes.LOGS}?level={Uri.EscapeDataString(level)}&take={take}", cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryListDto> GetRepositoriesAsync(bool askOrigin = true, CancellationToken cancellationToken = default)
        => GetAsync<RepositoryListDto>($"{ApiRoutes.REPOSITORIES}?origin={(askOrigin ? "true" : "false")}", cancellationToken);

    /// <inheritdoc />
    public Task<RepositoryListDto> AddRepositoryAsync(string name, string path, CancellationToken cancellationToken = default)
        => PostAsync<RepositoryListDto>(ApiRoutes.REPOSITORIES, new AddRepositoryRequest(name, path), cancellationToken);

    /// <inheritdoc />
    public async Task<RepositoryListDto> RemoveRepositoryAsync(string name, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(ApiRoutes.RepositoryPath(name), cancellationToken);
        return await ReadAsync<RepositoryListDto>(response, cancellationToken);
    }

    /// <inheritdoc />
    public Task<RepositoryListDto> ActivateRepositoryAsync(string name, CancellationToken cancellationToken = default)
        => PostAsync<RepositoryListDto>(ApiRoutes.RepositoryActivatePath(name), null, cancellationToken);

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose()
    {
        _long.Dispose();
        _http.Dispose();
    }

    /// <summary>
    /// Sends a GET request.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <returns>The response body.</returns>
    private async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await _http.GetAsync(path, cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Sends a POST request.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="path">The path.</param>
    /// <param name="body">The body, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the request.</param>
    /// <param name="longRunning">Whether the operation may take long and must not be cut off by the ordinary time limit.</param>
    /// <returns>The response body.</returns>
    private async Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken, bool longRunning = false)
    {
        var client = longRunning ? _long : _http;
        using var response = body is null
            ? await client.PostAsync(path, null, cancellationToken)
            : await client.PostAsync(path, JsonContent.Create(body, body.GetType(), options: _json), cancellationToken);
        return await ReadAsync<T>(response, cancellationToken);
    }

    /// <summary>
    /// Reads a successful JSON response.
    /// </summary>
    /// <typeparam name="T">The response type.</typeparam>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The body.</returns>
    /// <exception cref="ServiceApiException">The response is an error or empty.</exception>
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(_json, cancellationToken)
            ?? throw new ServiceApiException(response.StatusCode, _localizer.Get("Api.EmptyResponse"));
    }

    /// <summary>
    /// Throws for error responses, using the error message of the service.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the response is successful.</returns>
    /// <exception cref="ServiceApiException">The response is an error.</exception>
    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = text;
        try
        {
            // Only an object with a text "error" is the error body of the service. Anything else that happens to be JSON
            // (a bare string, an array, the problem details of the framework) is shown as it came: asking a value that
            // is no object for a property, or a property that is no string for its text, would throw here instead.
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
            {
                message = error.GetString() ?? text;
            }
        }
        catch (JsonException)
        {
            // Not a JSON error body; use the raw text.
        }

        throw new ServiceApiException(response.StatusCode, string.IsNullOrWhiteSpace(message) ? response.ReasonPhrase ?? response.StatusCode.ToString() : message);
    }
}
