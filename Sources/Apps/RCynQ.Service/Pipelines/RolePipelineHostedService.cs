using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Config;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Starts and stops the pipeline of the configured role with the host.
/// </summary>
/// <remarks>
/// On stop every active transport link is stopped as well, whatever the role, so no tunnel outlives the service.
/// </remarks>
public sealed class RolePipelineHostedService : IHostedService
{
    /// <summary>The pipeline factory.</summary>
    private readonly IRolePipelineFactory _factory;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link manager.</summary>
    private readonly ILinkManager _links;

    /// <summary>The logger.</summary>
    private readonly ILogger<RolePipelineHostedService> _logger;

    /// <summary>The running pipeline.</summary>
    private IRolePipeline? _pipeline;

    /// <summary>
    /// Initializes a new instance of the <see cref="RolePipelineHostedService"/> class.
    /// </summary>
    /// <param name="factory">The pipeline factory.</param>
    /// <param name="node">The local node.</param>
    /// <param name="links">The link manager.</param>
    /// <param name="logger">The logger.</param>
    public RolePipelineHostedService(IRolePipelineFactory factory, NodeConfig node, ILinkManager links, ILogger<RolePipelineHostedService> logger)
    {
        _factory = factory;
        _node = node;
        _links = links;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _pipeline = _factory.Create(_node.Role);
        try
        {
            await _pipeline.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The {Role} pipeline failed to start; the API stays available for diagnostics.", _node.Role);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_pipeline is not null)
        {
            await _pipeline.StopAsync(cancellationToken);
        }

        try
        {
            var stopped = await _links.StopAllAsync(cancellationToken);
            if (stopped.Count > 0)
            {
                _logger.LogInformation("Stopped {Count} transport link(s).", stopped.Count);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Some transport links could not be stopped cleanly.");
        }
    }
}
