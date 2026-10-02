using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Security;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.GitChannel;

/// <summary>
/// Asks the Origin endpoint for the branches of its repository and stores the answer as a bundle file.
/// </summary>
/// <remarks>
/// The request names the commits this node already has, so Origin answers with only what is new. The body is streamed to disk;
/// nothing is held in memory, and the download has no total time limit, only a limit for Origin to build the bundle and answer.
/// </remarks>
public sealed class GitBundleClient : IDisposable
{
    /// <summary>
    /// How long Origin may take to build the bundle and start the answer.
    /// </summary>
    public static readonly TimeSpan ResponseStartTimeout = TimeSpan.FromMinutes(10);

    /// <summary>JSON options for requests and responses.</summary>
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The connection factory.</summary>
    private readonly PeerLinkFactory _links;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitBundleClient"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="links">The connection factory.</param>
    /// <param name="node">The local node.</param>
    /// <param name="tokens">The link token provider.</param>
    public GitBundleClient(ILocalizer localizer, PeerLinkFactory links, NodeConfig node, LinkTokenProvider tokens)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = links;
        _node = node;
        _tokens = tokens;
    }

    /// <summary>
    /// Downloads the bundle with everything new on Origin.
    /// </summary>
    /// <param name="chain">The chain to Origin.</param>
    /// <param name="have">The commits this node already has; empty to get the whole repository.</param>
    /// <param name="targetDirectory">The directory for the bundle file.</param>
    /// <param name="repository">The name of the repository to ask for, or <see langword="null"/> for the one the peer works with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The downloaded bundle, or <see langword="null"/> when this node is up to date.</returns>
    /// <exception cref="SyncBridgeException">Origin refused the request or could not be reached.</exception>
    public async Task<GitBundleDownload?> DownloadAsync(IReadOnlyList<ChainLinkConfig> chain, IReadOnlyList<string> have, string targetDirectory, string? repository = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(have);
        var route = ChainRoute.Resolve(chain);
        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new SyncBridgeException(_localizer.Get("Send.NoToken"));
        }

        Directory.CreateDirectory(targetDirectory);
        var path = Path.Combine(targetDirectory, $"bundle-{Guid.NewGuid():N}.bundle");
        try
        {
            await using var link = await _links.OpenAsync(route, cancellationToken);
            using var request = link.CreateRequest(HttpMethod.Post, ApiRoutes.GIT_BUNDLE, _node.Id.Value, token);
            request.Content = JsonContent.Create(new GitBundleRequest(have, repository), options: _jsonOptions);

            using var startTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startTimeout.CancelAfter(ResponseStartTimeout);
            using var response = await link.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, startTimeout.Token);
            startTimeout.CancelAfter(Timeout.InfiniteTimeSpan);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await RefusalAsync(response, cancellationToken);
            }

            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await response.Content.CopyToAsync(file, cancellationToken);
            }

            var head = response.Headers.TryGetValues(ApiHeaders.GIT_HEAD, out var values) ? values.FirstOrDefault() : null;
            return new GitBundleDownload(path, string.IsNullOrWhiteSpace(head) ? null : head);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            Delete(path);
            var destination = route.UsesTls ? $"{route.Endpoint.Peer.Id} ({route.RemoteHost}:{route.RemotePort})" : $"{route.Target}";
            throw new SyncBridgeException(_localizer.Format("Git.BundleNotDelivered", destination, _localizer.Describe(ex)), ex);
        }
        catch
        {
            Delete(path);
            throw;
        }
    }

    /// <summary>
    /// Disposes the connections.
    /// </summary>
    public void Dispose() => _links.Dispose();

    /// <summary>
    /// Builds the error for a refused request.
    /// </summary>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The exception.</returns>
    private async Task<SyncBridgeException> RefusalAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        string reason;
        try
        {
            var rejection = string.IsNullOrWhiteSpace(text) ? null : JsonSerializer.Deserialize<PatchRejectionResponse>(text, _jsonOptions);
            reason = rejection is null ? text : $"{rejection.Reason}: {rejection.Error}";
        }
        catch (JsonException)
        {
            reason = text;
        }

        // An Origin of an older version has no such route and answers with an empty or a generic 404.
        if (response.StatusCode == HttpStatusCode.NotFound && !text.Contains("\"reason\"", StringComparison.OrdinalIgnoreCase))
        {
            return new SyncBridgeException(_localizer.Get("Git.BundleOlderPeer"));
        }

        return new SyncBridgeException(_localizer.Format("Git.BundleRefused", (int)response.StatusCode, reason).Trim());
    }

    /// <summary>
    /// Deletes a temporary file, ignoring failures.
    /// </summary>
    /// <param name="path">The file.</param>
    private static void Delete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Temporary data of the node; the next cleanup removes it.
        }
    }
}
