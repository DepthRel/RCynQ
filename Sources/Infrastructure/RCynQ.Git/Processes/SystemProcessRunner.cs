using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Git.Processes;

/// <summary>
/// <see cref="IProcessRunner"/> based on <see cref="Process"/>.
/// </summary>
/// <remarks>
/// Arguments are passed through <see cref="ProcessStartInfo.ArgumentList"/>, so no shell is involved and no
/// quoting of untrusted values is necessary. Standard input is closed immediately so child processes never wait
/// for interactive input. On timeout or cancellation the whole process tree is killed.
/// </remarks>
public sealed class SystemProcessRunner : IProcessRunner
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemProcessRunner"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public SystemProcessRunner(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public async Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        using var process = new Process { StartInfo = CreateStartInfo(spec) };
        StartOrThrow(process, spec);
        process.StandardInput.Close();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource();
        if (spec.Timeout is { } limit)
        {
            timeout.CancelAfter(limit);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token).ConfigureAwait(false);

            // The output ends only when every process that holds the pipes is gone. A child that outlives the command
            // (an SSH connection kept open, a credential helper) would keep these reads waiting for ever, so they are
            // bound by the same timeout and cancellation as the process itself.
            var output = await stdout.WaitAsync(linked.Token).ConfigureAwait(false);
            var error = await stderr.WaitAsync(linked.Token).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, output, error);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            Forget(stdout);
            Forget(stderr);
            if (!cancellationToken.IsCancellationRequested)
            {
                throw new SyncBridgeException(_localizer.Format("Process.TimedOut", spec.FileName, spec.Timeout));
            }

            throw;
        }
    }

    /// <summary>
    /// Observes the failure of an abandoned read, which ends with an error once the process and its streams are disposed.
    /// </summary>
    /// <param name="read">The abandoned read.</param>
    private static void Forget(Task read)
        => read.ContinueWith(static task => _ = task.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    /// <inheritdoc />
    public IRunningProcess Start(ProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var process = new Process { StartInfo = CreateStartInfo(spec), EnableRaisingEvents = true };
        var running = new RunningProcess(process);
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, e) => running.AppendError(e.Data);
        try
        {
            StartOrThrow(process, spec);
        }
        catch
        {
            process.Dispose();
            throw;
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return running;
    }

    /// <summary>
    /// Creates the start info for a specification.
    /// </summary>
    /// <param name="spec">The specification.</param>
    /// <returns>The start info.</returns>
    private static ProcessStartInfo CreateStartInfo(ProcessSpec spec)
    {
        var info = new ProcessStartInfo(spec.FileName)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        foreach (var argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (spec.WorkingDirectory is not null)
        {
            info.WorkingDirectory = spec.WorkingDirectory;
        }

        if (spec.EnvironmentVariables is not null)
        {
            foreach (var (key, value) in spec.EnvironmentVariables)
            {
                info.Environment[key] = value;
            }
        }

        return info;
    }

    /// <summary>
    /// Starts the process and converts start failures into <see cref="SyncBridgeException"/>.
    /// </summary>
    /// <param name="process">The process.</param>
    /// <param name="spec">The specification.</param>
    /// <exception cref="SyncBridgeException">The process could not be started.</exception>
    private void StartOrThrow(Process process, ProcessSpec spec)
    {
        try
        {
            if (!process.Start())
            {
                throw new SyncBridgeException(_localizer.Format("Process.NotStarted", spec.FileName));
            }
        }
        catch (Win32Exception ex)
        {
            throw new SyncBridgeException(_localizer.Format("Process.NotStartedBecause", spec.FileName, _localizer.Describe(ex)), ex);
        }
        catch (InvalidOperationException ex)
        {
            throw new SyncBridgeException(_localizer.Format("Process.NotStartedBecause", spec.FileName, _localizer.Describe(ex)), ex);
        }
    }

    /// <summary>
    /// Kills a process tree, ignoring processes that already exited.
    /// </summary>
    /// <param name="process">The process.</param>
    internal static void KillQuietly(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
        catch (Win32Exception)
        {
            // The process is exiting or access was denied; nothing else can be done.
        }
    }

    /// <summary>
    /// <see cref="IRunningProcess"/> wrapper around <see cref="Process"/>.
    /// </summary>
    private sealed class RunningProcess : IRunningProcess
    {
        /// <summary>
        /// Maximum number of characters of standard error kept in memory.
        /// </summary>
        private const int MaxErrorLength = 64 * 1024;

        /// <summary>
        /// The wrapped process.
        /// </summary>
        private readonly Process _process;

        /// <summary>
        /// Captured standard error.
        /// </summary>
        private readonly StringBuilder _error = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="RunningProcess"/> class.
        /// </summary>
        /// <param name="process">The process.</param>
        public RunningProcess(Process process)
        {
            _process = process;
        }

        /// <inheritdoc />
        public bool HasExited
        {
            get
            {
                try
                {
                    return _process.HasExited;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
            }
        }

        /// <inheritdoc />
        public int? ExitCode => HasExited ? SafeExitCode() : null;

        /// <inheritdoc />
        public string StandardError
        {
            get
            {
                lock (_error)
                {
                    return _error.ToString();
                }
            }
        }

        /// <inheritdoc />
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _process.WaitForExitAsync(cancellationToken);

        /// <inheritdoc />
        public void Kill() => KillQuietly(_process);

        /// <inheritdoc />
        public void Dispose()
        {
            Kill();
            _process.Dispose();
        }

        /// <summary>
        /// Appends a line of standard error.
        /// </summary>
        /// <param name="line">The line, or <see langword="null"/> at end of stream.</param>
        public void AppendError(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (_error)
            {
                if (_error.Length < MaxErrorLength)
                {
                    _error.AppendLine(line);
                }
            }
        }

        /// <summary>
        /// Reads the exit code without throwing.
        /// </summary>
        /// <returns>The exit code, or <c>-1</c> when unavailable.</returns>
        private int SafeExitCode()
        {
            try
            {
                return _process.ExitCode;
            }
            catch (InvalidOperationException)
            {
                return -1;
            }
        }
    }
}
