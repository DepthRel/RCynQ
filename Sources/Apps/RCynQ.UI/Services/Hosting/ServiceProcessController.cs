using System.ComponentModel;
using System.Globalization;
using RCynQ.Localization;
using RCynQ.UI.Enums;
using RCynQ.UI.Services.Api;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// <see cref="IServiceController"/> that runs RCynQ.Service as a child process of the UI.
/// </summary>
/// <remarks>
/// The service receives <c>--parent-pid</c>, so it also stops when the UI crashes. On a normal exit the UI asks the
/// service to shut down through the API, which stops pipelines, transports and SSH forwards, and kills the process
/// tree only when it does not exit in time.
/// </remarks>
public sealed class ServiceProcessController : IServiceController, IDisposable
{
    /// <summary>The connection settings.</summary>
    private readonly Connection.ServiceConnectionSettings _settings;

    /// <summary>The API client.</summary>
    private readonly IServiceApiClient _api;

    /// <summary>The process launcher.</summary>
    private readonly IChildProcessLauncher _launcher;

    /// <summary>The launch command, or <see langword="null"/> when the service was not found.</summary>
    private readonly ServiceLaunchCommand? _command;

    /// <summary>The timing options.</summary>
    private readonly ServiceControllerOptions _options;

    /// <summary>Serializes start, restart and stop.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>The service process started by this controller.</summary>
    private IChildProcess? _process;

    /// <summary>The current status.</summary>
    private ServiceControllerStatus _status = ServiceControllerStatus.NotStarted;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>The source of the time the start of the service is measured with.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceProcessController"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="settings">The connection settings with the configuration path.</param>
    /// <param name="api">The API client of the service.</param>
    /// <param name="launcher">The process launcher.</param>
    /// <param name="command">The launch command, or <see langword="null"/> when the service was not found.</param>
    /// <param name="options">The timing options, or <see langword="null"/> for defaults.</param>
    /// <param name="timeProvider">The source of the current time, or <see langword="null"/> for the system clock.</param>
    public ServiceProcessController(
        ILocalizer localizer,
        Connection.ServiceConnectionSettings settings,
        IServiceApiClient api,
        IChildProcessLauncher launcher,
        ServiceLaunchCommand? command,
        ServiceControllerOptions? options = null,
        TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _api = api ?? throw new ArgumentNullException(nameof(api));
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _command = command;
        _options = options ?? new ServiceControllerOptions();
    }

    /// <inheritdoc />
    public event EventHandler<ServiceControllerStatus>? StatusChanged;

    /// <inheritdoc />
    public string ConfigPath => _settings.ConfigPath;

    /// <inheritdoc />
    public ServiceControllerStatus Status
    {
        get => _status;
        private set
        {
            if (_status != value)
            {
                _status = value;
                StatusChanged?.Invoke(this, value);
            }
        }
    }

    /// <inheritdoc />
    public bool IsManaged => _process is { HasExited: false };

    /// <inheritdoc />
    public async Task<ServiceStartResult> EnsureRunningAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await EnsureRunningCoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ServiceStartResult> RestartAsync(CancellationToken cancellationToken = default)
    {
        if (!IsManaged && Status == ServiceControllerStatus.Attached)
        {
            return new ServiceStartResult(ServiceStartOutcome.NotManaged, _localizer.Get("Host.NotManaged"));
        }

        await StopAsync(cancellationToken).ConfigureAwait(false);
        return await EnsureRunningAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var process = _process;
            if (process is null)
            {
                return;
            }

            if (!process.HasExited)
            {
                await RequestShutdownAsync(cancellationToken).ConfigureAwait(false);
                using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                wait.CancelAfter(_options.StopTimeout);
                try
                {
                    await process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    process.Kill();
                }
            }

            ReleaseProcess();
            Status = ServiceControllerStatus.Stopped;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Releases the process handle without stopping the process.
    /// </summary>
    public void Dispose()
    {
        _process?.Dispose();
        _process = null;
        _gate.Dispose();
    }

    /// <summary>
    /// Makes the service available; the caller holds the gate.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result.</returns>
    private async Task<ServiceStartResult> EnsureRunningCoreAsync(CancellationToken cancellationToken)
    {
        var healthy = await _api.IsHealthyAsync(cancellationToken).ConfigureAwait(false);
        if (healthy)
        {
            if (IsManaged)
            {
                Status = ServiceControllerStatus.Running;
                return new ServiceStartResult(ServiceStartOutcome.Started, _localizer.Format("Host.Started", _process!.Id));
            }

            Status = ServiceControllerStatus.Attached;
            return new ServiceStartResult(ServiceStartOutcome.AlreadyRunning, _localizer.Get("Host.Attached"));
        }

        if (!File.Exists(ConfigPath))
        {
            return new ServiceStartResult(ServiceStartOutcome.NotConfigured, _localizer.Format("Host.NoConfig", ConfigPath));
        }

        if (_command is null)
        {
            Status = ServiceControllerStatus.Failed;
            return new ServiceStartResult(ServiceStartOutcome.NotFound, _localizer.Format("Host.NotFound", ServiceExecutableLocator.ASSEMBLY_NAME, ServiceExecutableLocator.ARGUMENT));
        }

        if (_process is not null)
        {
            _process.Kill();
            ReleaseProcess();
        }

        Status = ServiceControllerStatus.Starting;
        var arguments = _command.PrefixArguments
            .Concat(new[] { "--config", ConfigPath, "--parent-pid", _options.ParentProcessId.ToString(CultureInfo.InvariantCulture), "--language", _localizer.Current.Code })
            .ToArray();

        IChildProcess process;
        try
        {
            process = _launcher.Start(_command.FileName, arguments, Path.GetDirectoryName(ConfigPath)!);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Status = ServiceControllerStatus.Failed;
            return new ServiceStartResult(ServiceStartOutcome.Failed, _localizer.Format("Host.StartFailed", _localizer.Describe(ex)));
        }

        _process = process;
        var started = _timeProvider.GetTimestamp();
        while (_timeProvider.GetElapsedTime(started) < _options.StartTimeout)
        {
            if (process.HasExited)
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                var exitCode = process.ExitCode;
                var errors = process.ErrorOutput.Trim();
                ReleaseProcess();
                Status = ServiceControllerStatus.Failed;
                return new ServiceStartResult(
                    ServiceStartOutcome.Failed,
                    errors.Length > 0 ? errors : _localizer.Format("Host.Exited", exitCode),
                    exitCode);
            }

            if (await _api.IsHealthyAsync(cancellationToken).ConfigureAwait(false))
            {
                Status = ServiceControllerStatus.Running;
                return new ServiceStartResult(ServiceStartOutcome.Started, _localizer.Format("Host.Started", process.Id));
            }

            await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
        }

        process.Kill();
        ReleaseProcess();
        Status = ServiceControllerStatus.Failed;
        return new ServiceStartResult(ServiceStartOutcome.Failed, _localizer.Format("Host.Timeout", _options.StartTimeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)));
    }

    /// <summary>
    /// Asks the service to stop gracefully; failures are ignored because the process is killed afterwards if needed.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task representing the operation.</returns>
    private async Task RequestShutdownAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_options.ShutdownRequestTimeout);
        try
        {
            await _api.ShutdownServiceAsync(timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or ServiceApiException || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // The service may not answer any more; waiting and killing below still stops it.
        }
    }

    /// <summary>
    /// Disposes the process handle and forgets the process.
    /// </summary>
    private void ReleaseProcess()
    {
        _process?.Dispose();
        _process = null;
    }
}
