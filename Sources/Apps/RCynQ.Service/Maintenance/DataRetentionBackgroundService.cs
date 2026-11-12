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
/// The value is read again every <see cref="CheckInterval"/>, so a value changed in the settings is used without a
/// restart, and a new value starts a cleanup at once. A cleanup that fails is logged and tried again on the next day:
/// it never stops the service.
/// </remarks>
public sealed class DataRetentionBackgroundService : BackgroundService
{
    /// <summary>
    /// The time between two cleanups.
    /// </summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>
    /// How often the service looks whether a cleanup is due or the number of days was changed.
    /// </summary>
    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The retention service.</summary>
    private readonly DataRetentionService _retention;

    /// <summary>The logger.</summary>
    private readonly ILogger<DataRetentionBackgroundService> _logger;

    /// <summary>The clock of the checks.</summary>
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="DataRetentionBackgroundService"/> class.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="retention">The retention service.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="time">The clock of the checks; <see cref="TimeProvider.System"/> when <see langword="null"/>.</param>
    public DataRetentionBackgroundService(SyncBridgeOptions options, DataRetentionService retention, ILogger<DataRetentionBackgroundService> logger, TimeProvider? time = null)
    {
        _options = options;
        _retention = retention;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>
    /// Tells whether a cleanup is due now.
    /// </summary>
    /// <param name="days">The number of days the node keeps data now; <c>0</c> or less keeps it.</param>
    /// <param name="lastDays">The number of days of the last cleanup, or <see langword="null"/> before the first one.</param>
    /// <param name="lastCleanup">When the last cleanup ran, or <see langword="null"/> before the first one.</param>
    /// <param name="now">The current time.</param>
    /// <returns><see langword="true"/> when data is removed by age and the last cleanup is a day old or used another number of days.</returns>
    public static bool IsDue(int days, int? lastDays, DateTimeOffset? lastCleanup, DateTimeOffset now)
    {
        if (days <= 0)
        {
            return false;
        }

        return lastCleanup is not { } last || lastDays != days || now - last >= Interval;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_options.RetentionDays <= 0)
        {
            _logger.LogInformation("RetentionDays is 0: stored data is kept until it is cleaned by hand.");
        }

        // Not on the thread that starts the host: the first cleanup may take a while on a node with a lot of old data.
        await Task.Yield();

        int? lastDays = null;
        DateTimeOffset? lastCleanup = null;
        using var timer = new PeriodicTimer(CheckInterval, _time);
        try
        {
            do
            {
                var days = _options.RetentionDays;
                var now = _time.GetUtcNow();
                if (IsDue(days, lastDays, lastCleanup, now))
                {
                    await CleanAsync(days, stoppingToken);
                    lastDays = days;
                    lastCleanup = now;
                }
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
    /// <param name="days">The age, in days, of the data that goes.</param>
    /// <param name="cancellationToken">The stop token.</param>
    /// <returns>A task representing the cleanup.</returns>
    private async Task CleanAsync(int days, CancellationToken cancellationToken)
    {
        try
        {
            var report = await _retention.CleanAsync(TimeSpan.FromDays(days), cancellationToken);
            if (report.RemovedAnything)
            {
                _logger.LogInformation(
                    "Removed data older than {Days} day(s): {Patches} patch(es), {History} history entries, {Runs} diagnostics run(s), {Backups} backup(s), {Temporary} temporary item(s); {Bytes} bytes freed.",
                    days, report.Patches, report.HistoryEntries, report.DiagnosticRuns, report.Backups, report.TemporaryItems, report.FreedBytes);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Removing old data failed; it is tried again in {Hours} hours.", Interval.TotalHours);
        }
    }
}
