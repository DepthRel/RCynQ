using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Sync.Routing;

namespace RCynQ.Service.Maintenance;

/// <summary>
/// Keeps every link on its best route: checks the routes at regular intervals, and at once when the route in use
/// seems not to answer.
/// </summary>
/// <remarks>
/// A link with a single address is not checked, so a node without alternative routes does nothing here but read its
/// links. A check that fails is logged and tried again at the next interval: it never stops the service.
/// </remarks>
public sealed class LinkRouteMonitor : BackgroundService
{
    /// <summary>Knows the routes and chooses among them.</summary>
    private readonly ILinkRouteSelector _routes;

    /// <summary>How the routes are checked.</summary>
    private readonly RouteCheckOptions _options;

    /// <summary>The logger.</summary>
    private readonly ILogger<LinkRouteMonitor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LinkRouteMonitor"/> class.
    /// </summary>
    /// <param name="routes">Knows the routes and chooses among them.</param>
    /// <param name="options">How the routes are checked.</param>
    /// <param name="logger">The logger.</param>
    public LinkRouteMonitor(ILinkRouteSelector routes, RouteCheckOptions options, ILogger<LinkRouteMonitor> logger)
    {
        _routes = routes;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Not on the thread that starts the host: a check waits for addresses that may not answer.
        await Task.Yield();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await _routes.CheckAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Checking the link routes failed; it is tried again in {Seconds} seconds.", _options.Interval.TotalSeconds);
                }

                await _routes.WaitForCheckAsync(_options.Interval, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }
}
