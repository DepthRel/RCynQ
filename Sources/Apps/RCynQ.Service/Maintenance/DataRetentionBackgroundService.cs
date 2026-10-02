using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Config;
using RCynQ.Sync.Maintenance;

namespace RCynQ.Service.Maintenance;

/// <summary>
/// Removes the finished data that is older than <see cref="SyncBridgeOptions.RetentionDays"/>: once when the service
/// has started, then once a day.
/// </summary>
/// <remarks>
/// With <c>RetentionDays</c> set to <c>0</c> nothing is removed by age; the data stays until it is cleaned by hand.
/// A cleanup that fails is logged and tried again on the next day: it never stops the service.
/// </remarks>
public sealed class DataRetentionBackgroundService : BackgroundService
{
    /// <summary>
    /// The time between two cleanups.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The retention service.</summary>
    private readonly DataRetentionService _retention;

    /// <summary>The logger.</summary>
    private readonly ILogger<DataRetentionBackgroundService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataRetentionBackgroundService"/> class.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="retention">The retention service.</param>
    /// <param name="logger">The logger.</param>
    public DataRetentionBackgroundService(SyncBridgeOptions options, DataRetentionService retention, ILogger<DataRetentionBackgroundService> logger)
    {
        _options = options;
        _retention = retention;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            _logger.LogInformation("RetentionDays is 0: stored data is kept until it is cleaned by hand.");
            return;
        }

        // Not on the thread that starts the host: the first cleanup may take a while on a node with a lot of old data.
        await Task.Yield();
        using var timer = new PeriodicTimer(Interval);
        try
        {
            do
            {
                await CleanAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    /// <summary>
    /// Runs one cleanup and logs what it removed.
    /// </summary>
    /// <param name="cancellationToken">The stop token.</param>
    /// <returns>A task representing the cleanup.</returns>
    private async Task CleanAsync(CancellationToken cancellationToken)
    {
        try
        {
            var report = await _retention.CleanAsync(TimeSpan.FromDays(_options.RetentionDays), cancellationToken);
            if (report.RemovedAnything)
            {
                _logger.LogInformation(
                    "Removed data older than {Days} day(s): {Patches} patch(es), {History} history entries, {Runs} diagnostics run(s), {Backups} backup(s), {Temporary} temporary item(s); {Bytes} bytes freed.",
                    _options.RetentionDays, report.Patches, report.HistoryEntries, report.DiagnosticRuns, report.Backups, report.TemporaryItems, report.FreedBytes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Removing old data failed; it is tried again in {Hours} hours.", Interval.TotalHours);
        }
    }
}
