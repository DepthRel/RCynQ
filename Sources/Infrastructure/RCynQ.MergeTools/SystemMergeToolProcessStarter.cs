using System.ComponentModel;
using System.Diagnostics;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.MergeTools;

/// <summary>
/// <see cref="IMergeToolProcessStarter"/> based on <see cref="Process"/>: no shell, every argument passed as it is, and
/// no redirected streams.
/// </summary>
/// <remarks>
/// A merge tool is a window, and its output is of no use. Redirecting it would also make waiting for the tool wait for
/// every process that inherited the streams: an editor that starts its long-lived main process on the way (VS Code, a
/// JetBrains IDE) would keep the wait going after the merge is closed.
/// </remarks>
public sealed class SystemMergeToolProcessStarter : IMergeToolProcessStarter
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemMergeToolProcessStarter"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public SystemMergeToolProcessStarter(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

    /// <inheritdoc />
    public IRunningProcess Start(ProcessSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        var info = new ProcessStartInfo(spec.FileName) { UseShellExecute = false };
        foreach (var argument in spec.Arguments)
        {
            info.ArgumentList.Add(argument);
        }

        if (spec.WorkingDirectory is not null)
        {
            info.WorkingDirectory = spec.WorkingDirectory;
        }

        var process = new Process { StartInfo = info };
        try
        {
            if (!process.Start())
            {
                throw new SyncBridgeException(_localizer.Format("Process.NotStarted", spec.FileName));
            }
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            process.Dispose();
            throw new SyncBridgeException(_localizer.Format("Process.NotStartedBecause", spec.FileName, _localizer.Describe(ex)), ex);
        }

        return new ToolProcess(process);
    }

    /// <summary>
    /// <see cref="IRunningProcess"/> of a merge tool: disposing it releases the handle, the window stays.
    /// </summary>
    private sealed class ToolProcess : IRunningProcess
    {
        /// <summary>The process.</summary>
        private readonly Process _process;

        /// <summary>
        /// Initializes a new instance of the <see cref="ToolProcess"/> class.
        /// </summary>
        /// <param name="process">The process.</param>
        public ToolProcess(Process process)
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
        public int? ExitCode
        {
            get
            {
                try
                {
                    return _process.HasExited ? _process.ExitCode : null;
                }
                catch (InvalidOperationException)
                {
                    return null;
                }
            }
        }

        /// <inheritdoc />
        public string StandardError => string.Empty;

        /// <inheritdoc />
        public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _process.WaitForExitAsync(cancellationToken);

        /// <inheritdoc />
        public void Kill()
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                // The tool already exited or cannot be stopped from here.
            }
        }

        /// <inheritdoc />
        public void Dispose() => _process.Dispose();
    }
}
