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
/// nothing is held in memory, and the download has no total time limit, only a limit for Origin to build the bundle and answer
/// and a limit for a pause in the middle of the body. A bundle larger than <see cref="ApiLimits.MAX_GIT_BUNDLE_BYTES"/> is
/// refused, so an Origin that never stops sending cannot fill the disk.
/// </remarks>
public sealed class GitBundleClient : IDisposable
{
    /// <summary>
    /// How long Origin may take to build the bundle and start the answer.
    /// </summary>
    public static readonly TimeSpan ResponseStartTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// How long the body of the answer may stand still before the download is given up.
    /// </summary>
    public static readonly TimeSpan BodyIdleTimeout = TimeSpan.FromMinutes(2);

    /// <summary>The size of the buffer the body is copied through.</summary>
    private const int BUFFER_SIZE = 81920;

    /// <summary>The longest list of branches read from Origin, in bytes; ten thousand branches take a small part of it.</summary>
    private const int MAX_BRANCH_LIST_BYTES = 16 * 1024 * 1024;

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
                throw Refusal(response.StatusCode, await ReadTextAsync(response.Content, ApiLimits.MAX_REFUSAL_BYTES, cancellationToken));
            }

            if (response.Content.Headers.ContentLength > ApiLimits.MAX_GIT_BUNDLE_BYTES)
            {
                throw new SyncBridgeException(_localizer.Format("Git.BundleTooLarge", ApiLimits.MAX_GIT_BUNDLE_BYTES / (1024 * 1024)));
            }

            await using (var body = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BUFFER_SIZE, useAsync: true))
            {
                await CopyBundleAsync(body, file, cancellationToken);
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
    /// Asks Origin for every branch of the repository with the commit it points at.
    /// </summary>
    /// <param name="chain">The chain to Origin.</param>
    /// <param name="repository">The name of the repository to ask for, or <see langword="null"/> for the one the peer works with.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The branches; <see langword="null"/> when Origin runs an older version that cannot list them.</returns>
    /// <exception cref="SyncBridgeException">Origin refused the request, answered nonsense or could not be reached.</exception>
    public async Task<GitBranchListResponse?> ListBranchesAsync(IReadOnlyList<ChainLinkConfig> chain, string? repository = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var route = ChainRoute.Resolve(chain);
        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new SyncBridgeException(_localizer.Get("Send.NoToken"));
        }

        try
        {
            await using var link = await _links.OpenAsync(route, cancellationToken);
            using var request = link.CreateRequest(HttpMethod.Post, ApiRoutes.GIT_BRANCHES, _node.Id.Value, token);
            request.Content = JsonContent.Create(new GitBranchListRequest(repository), options: _jsonOptions);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(ResponseStartTimeout);
            using var response = await link.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                var text = await ReadTextAsync(response.Content, ApiLimits.MAX_REFUSAL_BYTES, timeout.Token);

                // An older Origin has no such route: its public port answers 404, its local API 401, neither with a reason.
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized &&
                    !text.Contains("\"reason\"", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                throw Refusal(response.StatusCode, text);
            }

            var list = await ReadTextAsync(response.Content, MAX_BRANCH_LIST_BYTES, timeout.Token);
            return JsonSerializer.Deserialize<GitBranchListResponse>(list, _jsonOptions)
                ?? throw new SyncBridgeException(_localizer.Format("Git.BundleRefused", (int)response.StatusCode, string.Empty).Trim());
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            var destination = route.UsesTls ? $"{route.Endpoint.Peer.Id} ({route.RemoteHost}:{route.RemotePort})" : $"{route.Target}";
            throw new SyncBridgeException(_localizer.Format("Git.BundleNotDelivered", destination, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Disposes the connections.
    /// </summary>
    public void Dispose() => _links.Dispose();

    /// <summary>
    /// Copies the bundle from the answer to the file, within <see cref="ApiLimits.MAX_GIT_BUNDLE_BYTES"/> and without a
    /// pause longer than <see cref="BodyIdleTimeout"/>.
    /// </summary>
    /// <param name="body">The body of the answer.</param>
    /// <param name="file">The bundle file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the whole bundle is on disk.</returns>
    /// <exception cref="SyncBridgeException">The bundle is larger than the limit.</exception>
    /// <exception cref="IOException">The body stood still for too long.</exception>
    private async Task CopyBundleAsync(Stream body, Stream file, CancellationToken cancellationToken)
    {
        var buffer = new byte[BUFFER_SIZE];
        long total = 0;
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        while (true)
        {
            int read;
            idle.CancelAfter(BodyIdleTimeout);
            try
            {
                read = await body.ReadAsync(buffer, idle.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new IOException(_localizer.Format("Git.BundleStalled", (int)BodyIdleTimeout.TotalSeconds));
            }

            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > ApiLimits.MAX_GIT_BUNDLE_BYTES)
            {
                throw new SyncBridgeException(_localizer.Format("Git.BundleTooLarge", ApiLimits.MAX_GIT_BUNDLE_BYTES / (1024 * 1024)));
            }

            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    /// <summary>
    /// Reads the text of an answer up to a number of bytes.
    /// </summary>
    /// <param name="content">The content of the answer.</param>
    /// <param name="maxBytes">The most bytes to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The text; a longer answer is cut at the limit, which spoils it as JSON.</returns>
    private static async Task<string> ReadTextAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var text = new MemoryStream();
        var buffer = new byte[BUFFER_SIZE];
        while (text.Length < maxBytes)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, maxBytes - text.Length)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            text.Write(buffer, 0, read);
        }

        return System.Text.Encoding.UTF8.GetString(text.GetBuffer(), 0, (int)text.Length);
    }

    /// <summary>
    /// Builds the error for a refused request.
    /// </summary>
    /// <param name="statusCode">The status of the answer.</param>
    /// <param name="text">The text of the answer.</param>
    /// <returns>The exception.</returns>
    private SyncBridgeException Refusal(HttpStatusCode statusCode, string text)
    {
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
        if (statusCode == HttpStatusCode.NotFound && !text.Contains("\"reason\"", StringComparison.OrdinalIgnoreCase))
        {
            return new SyncBridgeException(_localizer.Get("Git.BundleOlderPeer"));
        }

        return new SyncBridgeException(_localizer.Format("Git.BundleRefused", (int)statusCode, reason).Trim());
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
