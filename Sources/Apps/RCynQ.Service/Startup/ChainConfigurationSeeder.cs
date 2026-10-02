using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Security;

namespace RCynQ.Service.Startup;

/// <summary>
/// Stores the local node and the chain links from configuration in the database on start.
/// </summary>
/// <remarks>
/// Links defined in <c>syncbridge.json</c> are authoritative: they are upserted by their identifier on every start, keeping what
/// an import stored about their peers and the file cannot state (see <see cref="ConfiguredLinkStore"/>).
/// Links added through the API or CLI are kept.
/// </remarks>
public sealed class ChainConfigurationSeeder : IHostedService
{
    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The node repository.</summary>
    private readonly INodeRepository _nodes;

    /// <summary>The store of the configured links.</summary>
    private readonly ConfiguredLinkStore _links;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The logger.</summary>
    private readonly ILogger<ChainConfigurationSeeder> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChainConfigurationSeeder"/> class.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="node">The local node.</param>
    /// <param name="nodes">The node repository.</param>
    /// <param name="links">The store of the configured links.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="logger">The logger.</param>
    public ChainConfigurationSeeder(SyncBridgeOptions options, NodeConfig node, INodeRepository nodes, ConfiguredLinkStore links, LinkTokenProvider tokens, ILogger<ChainConfigurationSeeder> logger)
    {
        _options = options;
        _node = node;
        _nodes = nodes;
        _links = links;
        _tokens = tokens;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _nodes.UpsertAsync(_node, cancellationToken);
        await _links.SeedAsync(_options, cancellationToken);

        if (_node.ParticipatesInPatchChannel && string.IsNullOrEmpty(await _tokens.GetTokenAsync(cancellationToken)))
        {
            _logger.LogWarning("No link token is configured; incoming patches will be refused until SyncBridge:LinkToken is set.");
        }

        _logger.LogInformation("Node {NodeId} ({Role}) started with {LinkCount} configured link(s).", _node.Id, _node.Role, _options.Chain.Count);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
