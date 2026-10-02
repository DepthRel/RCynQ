using Microsoft.Extensions.Logging;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Service.Pipelines;

/// <summary>
/// Relay pipeline: only transport links and their status. It has no patch or Git logic.
/// </summary>
/// <remarks>
/// Links are started in the background and their state is refreshed periodically, which publishes changes and fails
/// over broken transports.
/// </remarks>
public sealed class RelayPipeline : IRolePipeline, IAsyncDisposable
{
    /// <summary>
    /// The link status refresh interval.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(30);

    /// <summary>The link manager.</summary>
    private readonly ILinkManager _links;

    /// <summary>The logger.</summary>
    private readonly ILogger<RelayPipeline> _logger;

    /// <summary>Stops the background loop.</summary>
    private CancellationTokenSource? _stopping;

    /// <summary>The background loop.</summary>
    private Task? _loop;

    /// <summary>
    /// Initializes a new instance of the <see cref="RelayPipeline"/> class.
    /// </summary>
    /// <param name="links">The link manager.</param>
    /// <param name="logger">The logger.</param>
    public RelayPipeline(ILinkManager links, ILogger<RelayPipeline> logger)
    {
        _links = links;
        _logger = logger;
    }

    /// <inheritdoc />
    public NodeRole Role => NodeRole.Relay;

    /// <inheritdoc />
    public RoleCapabilities Capabilities => RoleCapabilities.For(Role);

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _stopping = new CancellationTokenSource();
        _loop = RunAsync(_stopping.Token);
        _logger.LogInformation("Relay pipeline started.");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_stopping is null || _loop is null)
        {
            return;
        }

        await _stopping.CancelAsync();
        try
        {
            await _loop.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown.
        }

        await _links.StopAllAsync(cancellationToken);
    }

    /// <summary>
    /// Stops the background loop.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_stopping is not null)
        {
            await _stopping.CancelAsync();
            _stopping.Dispose();
        }
    }

    /// <summary>
    /// Starts all links and refreshes their state until stopped.
    /// </summary>
    /// <param name="cancellationToken">The stop token.</param>
    /// <returns>A task representing the loop.</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        await Task.Yield();
        try
        {
            await SuperviseAsync(() => _links.StartAllAsync(cancellationToken), "start");
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(RefreshInterval, cancellationToken);
                await SuperviseAsync(
                    async () =>
                    {
                        foreach (var state in await _links.GetStatusAsync(cancellationToken))
                        {
                            if (!state.IsUsable && state.Status != LinkStatus.Stopped)
                            {
                                await _links.TryFailoverAsync(state.LinkId, cancellationToken);
                            }
                        }
                    },
                    "refresh");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    /// <summary>
    /// Runs one round of the loop. A failing round is logged and the loop goes on: a link removed between the status and
    /// the failover, or a database that is busy for a moment, must not end the supervision of every link for good.
    /// </summary>
    /// <param name="round">The work of the round.</param>
    /// <param name="name">The name of the round, for the log.</param>
    /// <returns>A task representing the round.</returns>
    private async Task SuperviseAsync(Func<Task> round, string name)
    {
        try
        {
            await round();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Relay link {Round} failed; the next round tries again.", name);
        }
    }
}
