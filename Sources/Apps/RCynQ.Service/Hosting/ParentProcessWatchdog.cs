using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RCynQ.Service.Hosting;

/// <summary>
/// Stops the service gracefully when the parent process exits, so transports and SSH forwards are closed even when
/// the UI that started the service crashed or was killed.
/// </summary>
public sealed class ParentProcessWatchdog : BackgroundService
{
    /// <summary>
    /// The default polling interval.
    /// </summary>
    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    /// <summary>The parent process.</summary>
    private readonly ParentProcessOptions _parent;

    /// <summary>The application lifetime.</summary>
    private readonly IHostApplicationLifetime _lifetime;

    /// <summary>The logger.</summary>
    private readonly ILogger<ParentProcessWatchdog> _logger;

    /// <summary>Checks whether a process is alive, or <see langword="null"/> for the process handle check.</summary>
    private readonly Func<int, bool>? _isAlive;

    /// <summary>The polling interval.</summary>
    private readonly TimeSpan _interval;

    /// <summary>The parent process handle; keeping it open prevents confusion with a reused identifier.</summary>
    private Process? _handle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentProcessWatchdog"/> class.
    /// </summary>
    /// <param name="parent">The parent process.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="logger">The logger.</param>
    public ParentProcessWatchdog(ParentProcessOptions parent, IHostApplicationLifetime lifetime, ILogger<ParentProcessWatchdog> logger)
        : this(parent, lifetime, logger, null, DefaultPollInterval)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ParentProcessWatchdog"/> class with an explicit liveness check.
    /// </summary>
    /// <param name="parent">The parent process.</param>
    /// <param name="lifetime">The application lifetime.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="isAlive">Checks whether a process is alive, or <see langword="null"/> for the process handle check.</param>
    /// <param name="interval">The polling interval.</param>
    public ParentProcessWatchdog(ParentProcessOptions parent, IHostApplicationLifetime lifetime, ILogger<ParentProcessWatchdog> logger, Func<int, bool>? isAlive, TimeSpan interval)
    {
        _parent = parent ?? throw new ArgumentNullException(nameof(parent));
        _lifetime = lifetime ?? throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _isAlive = isAlive;
        _interval = interval > TimeSpan.Zero ? interval : throw new ArgumentOutOfRangeException(nameof(interval));
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _handle?.Dispose();
        base.Dispose();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        try
        {
            do
            {
                if (!IsParentAlive())
                {
                    _logger.LogInformation("Parent process {ProcessId} exited; stopping the service.", _parent.ProcessId);
                    _lifetime.StopApplication();
                    return;
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
    /// Checks whether the parent process is still running.
    /// </summary>
    /// <returns><see langword="true"/> while the parent runs or its state cannot be determined.</returns>
    private bool IsParentAlive()
    {
        if (_isAlive is not null)
        {
            return _isAlive(_parent.ProcessId);
        }

        try
        {
            _handle ??= Process.GetProcessById(_parent.ProcessId);
            return !_handle.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // Without access to the parent the service keeps running rather than stopping unexpectedly.
            return true;
        }
    }
}
