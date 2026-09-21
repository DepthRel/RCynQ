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
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The HTTP client.</summary>
    private readonly HttpClient _http;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceApiClient"/> class.
    /// </summary>
    /// <param name="settings">The connection settings.</param>
    /// <param name="tokens">The token provider, or <see langword="null"/> to create one from <paramref name="settings"/>.</param>
    /// <param name="handler">The HTTP handler, or <see langword="null"/> for a handler that bypasses the system proxy.</param>
    public ServiceApiClient(ServiceConnectionSettings settings, ApiTokenProvider? tokens = null, HttpMessageHandler? handler = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var tokenHandler = new ApiTokenHandler(tokens ?? new ApiTokenProvider(settings), handler ?? new SocketsHttpHandler { UseProxy = false });
        _http = new HttpClient(tokenHandler)
        {
            BaseAddress = settings.ServiceUri,
            Timeout = TimeSpan.FromMinutes(10)
        };
    }

    /// <inheritdoc />
    public Task<NodeConfigurationDto> GetConfigAsync(CancellationToken cancellationToken = default) => GetAsync<NodeConfigurationDto>(ApiRoutes.Config, cancellationToken);

    /// <inheritdoc />
    public Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default) => GetAsync<NodeSyncStatus>(ApiRoutes.State, cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<PatchMetadata>> GetPatchesAsync(int take, CancellationToken cancellationToken = default)
        => await GetAsync<PatchMetadata[]>($"{ApiRoutes.Patches}?take={take}", cancellationToken);

    /// <inheritdoc />
    public async Task<string> GetPatchDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.GetAsync(ApiRoutes.PatchDiffPath(patchId), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    /// <inheritdoc />
    public Task<SendPatchResult> SendPatchAsync(CancellationToken cancellationToken = default) => PostAsync<SendPatchResult>(ApiRoutes.SendPatch, null, cancellationToken);

    /// <inheritdoc />
    public Task<SnapshotInfo> CreateBaselineAsync(CancellationToken cancellationToken = default) => PostAsync<SnapshotInfo>(ApiRoutes.Baseline, null, cancellationToken);

    /// <inheritdoc />
    public Task<PatchApplyResult> ResolvePatchAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
        => PostAsync<PatchApplyResult>(ApiRoutes.ResolvePatchPath(patchId), new ResolvePatchRequest(policy), cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkDto>> GetLinksAsync(CancellationToken cancellationToken = default) => await GetAsync<LinkDto[]>(ApiRoutes.Links, cancellationToken);

    /// <inheritdoc />
    public Task<LinkDto> SaveLinkAsync(ChainLinkOptions link, CancellationToken cancellationToken = default) => PostAsync<LinkDto>(ApiRoutes.Links, link, cancellationToken);

    /// <inheritdoc />
    public async Task DeleteLinkAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        using var response = await _http.DeleteAsync(ApiRoutes.LinkPath(linkId), cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LinkState>> GetLinkStatusAsync(CancellationToken cancellationToken = default) => await GetAsync<LinkState[]>(ApiRoutes.LinkStatus, cancellationToken);

    /// <inheritdoc />
    public Task<LinkState> StartLinkAsync(Guid linkId, CancellationToken cancellationToken = default) => PostAsync<LinkState>(ApiRoutes.LinkUpPath(linkId), null, cancellationToken);

    /// <inheritdoc />
    public Task<LinkState> StopLinkAsync(Guid linkId, CancellationToken cancellationToken = default) => PostAsync<LinkState>(ApiRoutes.LinkDownPath(linkId), null, cancellationToken);

    /// <inheritdoc />
    public Task<GitBranchInfo> GetBranchAsync(CancellationToken cancellationToken = default) => GetAsync<GitBranchInfo>(ApiRoutes.GitBranch, cancellationToken);

    /// <inheritdoc />
    public Task<GitOperationResult> FetchAsync(CancellationToken cancellationToken = default) => PostAsync<GitOperationResult>(ApiRoutes.GitFetch, null, cancellationToken);

    /// <inheritdoc />
    public Task<GitOperationResult> PullAsync(CancellationToken cancellationToken = default) => PostAsync<GitOperationResult>(ApiRoutes.GitPull, null, cancellationToken);

    /// <inheritdoc />
    public Task<DiagnosticsEvent> RunDiagnosticsAsync(CancellationToken cancellationToken = default) => PostAsync<DiagnosticsEvent>(ApiRoutes.DiagnosticsRun, null, cancellationToken);

    /// <inheritdoc />
    public Task<AutoFixResult> ApplyFixAsync(DiagnosticFix fix, bool confirmed, CancellationToken cancellationToken = default)
        => PostAsync<AutoFixResult>(ApiRoutes.DiagnosticsFix, new ApplyFixRequest(fix, confirmed), cancellationToken);

    /// <inheritdoc />
    public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            using var response = await _http.GetAsync(ApiRoutes.Health, timeout.Token).ConfigureAwait(false);
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
        using var response = await _http.PostAsync(ApiRoutes.ServiceShutdown, null, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<NodeEnvironmentReport> ProbeExportAsync(CancellationToken cancellationToken = default)
        => GetAsync<NodeEnvironmentReport>(ApiRoutes.ConfigExportProbe, cancellationToken);

    /// <inheritdoc />
    public Task<PeerAccessResult> AllowPeerAccessAsync(CancellationToken cancellationToken = default)
        => PostAsync<PeerAccessResult>(ApiRoutes.SshAuthorizedKeys, null, cancellationToken);

    /// <inheritdoc />
    public Task<NodeEnvironmentReport> GenerateSshKeyAsync(CancellationToken cancellationToken = default)
        => PostAsync<NodeEnvironmentReport>(ApiRoutes.ConfigSshKey, null, cancellationToken);

    /// <inheritdoc />
    public Task<ExportedNodeConfiguration> ExportConfigAsync(string? host, string? user, int sshPort, string? sshPublicKey, string? hostKeyFingerprint, CancellationToken cancellationToken = default)
    {
        var query = new List<string> { $"port={sshPort}" };
        if (!string.IsNullOrWhiteSpace(sshPublicKey))
        {
            query.Add("sshPublicKey=" + Uri.EscapeDataString(sshPublicKey));
        }

        if (!string.IsNullOrWhiteSpace(hostKeyFingerprint))
        {
            query.Add("hostKeyFingerprint=" + Uri.EscapeDataString(hostKeyFingerprint));
        }

        if (!string.IsNullOrWhiteSpace(host))
        {
            query.Add("host=" + Uri.EscapeDataString(host));
        }

        if (!string.IsNullOrWhiteSpace(user))
        {
            query.Add("user=" + Uri.EscapeDataString(user));
        }

        return GetAsync<ExportedNodeConfiguration>($"{ApiRoutes.ConfigExport}?{string.Join('&', query)}", cancellationToken);
    }

    /// <inheritdoc />
    public Task<ImportResult> ImportConfigAsync(ExportedNodeConfiguration configuration, CancellationToken cancellationToken = default)
        => PostAsync<ImportResult>(ApiRoutes.ConfigImport, configuration, cancellationToken);

    /// <inheritdoc />
    public async Task<RoleChangeResult> SetRoleAsync(SetRoleRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await _http.PutAsJsonAsync(ApiRoutes.ConfigRole, request, Json, cancellationToken);
        return await ReadAsync<RoleChangeResult>(response, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LogEntryDto>> GetLogsAsync(string level, int take, CancellationToken cancellationToken = default)
        => await GetAsync<LogEntryDto[]>($"{ApiRoutes.Logs}?level={Uri.EscapeDataString(level)}&take={take}", cancellationToken);

    /// <summary>
    /// Disposes the HTTP client.
    /// </summary>
    public void Dispose() => _http.Dispose();

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
    /// <returns>The response body.</returns>
    private async Task<T> PostAsync<T>(string path, object? body, CancellationToken cancellationToken)
    {
        using var response = body is null
            ? await _http.PostAsync(path, null, cancellationToken)
            : await _http.PostAsync(path, JsonContent.Create(body, body.GetType(), options: Json), cancellationToken);
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
    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<T>(Json, cancellationToken)
            ?? throw new ServiceApiException(response.StatusCode, Texts.Get("Api.EmptyResponse"));
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
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.TryGetProperty("error", out var error))
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
