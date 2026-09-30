using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Config;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Starts and stops the pipeline of the configured role with the host.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline is started from <see cref="IHostedLifecycleService.StartedAsync"/>, not from
/// <see cref="IHostedService.StartAsync"/>, because the latter runs before the Kestrel listener is bound: ASP.NET Core
/// registers its own hosted service for the web host while the application is built, that is after every
/// <c>AddHostedService</c> call of the composition, so the local API would not answer until this service returned.
/// Starting a pipeline scans the whole working tree and exports the sync base, which takes far longer than the time a
/// client such as the UI waits for <c>GET /health</c>; done here it would make the service look dead on every start.
/// </para>
/// <para>
/// The warm-up is cancelled when the application begins to stop, so a shutdown does not wait for a scan to finish.
/// On stop every active transport link is stopped as well, whatever the role, so no tunnel outlives the service.
/// </para>
/// </remarks>
public sealed class RolePipelineHostedService : IHostedLifecycleService
{
    /// <summary>The pipeline factory.</summary>
    private readonly IRolePipelineFactory _factory;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The link manager.</summary>
    private readonly ILinkManager _links;

    /// <summary>The application lifetime, used to abort the warm-up on shutdown.</summary>
    private readonly IHostApplicationLifetime _lifetime;

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
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="logger">The logger.</param>
    public RolePipelineHostedService(
        IRolePipelineFactory factory,
        NodeConfig node,
        ILinkManager links,
        IHostApplicationLifetime lifetime,
        ILogger<RolePipelineHostedService> logger)
    {
        _factory = factory;
        _node = node;
        _links = links;
        _lifetime = lifetime;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Creates the pipeline of the configured role without starting it, so the listener is not held back.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A completed task.</returns>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _pipeline = _factory.Create(_node.Role);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Starts the pipeline after the listener is up; failures are logged and leave the API available for diagnostics.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    public async Task StartedAsync(CancellationToken cancellationToken)
    {
        if (_pipeline is null)
        {
            return;
        }

        using var warmup = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.ApplicationStopping);
        try
        {
            await _pipeline.StartAsync(warmup.Token);
        }
        catch (OperationCanceledException) when (warmup.IsCancellationRequested)
        {
            _logger.LogInformation("The {Role} pipeline start was cancelled because the service is stopping.", _node.Role);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The {Role} pipeline failed to start; the API stays available for diagnostics.", _node.Role);
        }
    }

    /// <inheritdoc />
    public Task StoppingAsync(CancellationToken cancellationToken) => Task.CompletedTask;

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

    /// <inheritdoc />
    public Task StoppedAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
