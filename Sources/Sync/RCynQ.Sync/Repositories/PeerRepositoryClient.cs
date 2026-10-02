using System.Text.Json;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Security;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Repositories;

/// <summary>
/// Asks the Origin which repositories it offers, through the same chain and the same endpoint as patches.
/// </summary>
public sealed class PeerRepositoryClient : IDisposable
{
    /// <summary>How long the Origin may take to answer.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>JSON options of the exchange.</summary>
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
    /// Initializes a new instance of the <see cref="PeerRepositoryClient"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="links">The connection factory.</param>
    /// <param name="node">The local node.</param>
    /// <param name="tokens">The link token provider.</param>
    public PeerRepositoryClient(ILocalizer localizer, PeerLinkFactory links, NodeConfig node, LinkTokenProvider tokens)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _links = links ?? throw new ArgumentNullException(nameof(links));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
    }

    /// <summary>
    /// Asks the Origin at the end of the chain for the names of its repositories.
    /// </summary>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer of the Origin.</returns>
    /// <exception cref="SyncBridgeException">The Origin could not be reached or refused; the message tells why.</exception>
    public async Task<PeerRepositoriesResponse> GetAsync(IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chain);
        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(chain);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
        }

        var token = await _tokens.GetTokenAsync(cancellationToken);
        if (string.IsNullOrEmpty(token))
        {
            throw new SyncBridgeException(_localizer.Get("Send.NoToken"));
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await using var link = await _links.OpenAsync(route, timeout.Token);
            using var request = link.CreateRequest(HttpMethod.Get, ApiRoutes.PEER_REPOSITORIES, _node.Id.Value, token);
            using var response = await link.Client.SendAsync(request, timeout.Token);
            var text = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                throw new SyncBridgeException(_localizer.Format("Repository.CatalogRefused", route.Endpoint.Peer.Id, Reason(text, (int)response.StatusCode)));
            }

            return JsonSerializer.Deserialize<PeerRepositoriesResponse>(text, _jsonOptions)
                ?? throw new SyncBridgeException(_localizer.Format("Repository.CatalogRefused", route.Endpoint.Peer.Id, "empty answer"));
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new SyncBridgeException(_localizer.Format("Repository.CatalogUnavailable", route.Endpoint.Peer.Id, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Releases the connections.
    /// </summary>
    public void Dispose() => _links.Dispose();

    /// <summary>
    /// Reads the reason of a refusal.
    /// </summary>
    /// <param name="text">The body of the answer.</param>
    /// <param name="statusCode">The status code of the answer.</param>
    /// <returns>The reason the Origin gave, or the status code.</returns>
    private static string Reason(string text, int statusCode)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(text) && JsonSerializer.Deserialize<PatchRejectionResponse>(text, _jsonOptions) is { Error.Length: > 0 } rejection)
            {
                return rejection.Error;
            }
        }
        catch (JsonException)
        {
            // Not an answer of RCynQ; the status code is all there is.
        }

        return "HTTP " + statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
