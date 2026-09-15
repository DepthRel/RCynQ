using System.Diagnostics;

namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// <see cref="IChildProcessLauncher"/> based on <see cref="Process"/>.
/// </summary>
public sealed class SystemChildProcessLauncher : IChildProcessLauncher
{
    /// <inheritdoc />
    public IChildProcess Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentNullException.ThrowIfNull(arguments);

        var info = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory
        };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        var child = new ChildProcess(process);
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException($"'{fileName}' did not start.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return child;
    }

    /// <summary>
    /// A started process with bounded capture of standard error.
    /// </summary>
    private sealed class ChildProcess : IChildProcess
    {
        /// <summary>The number of standard error lines kept.</summary>
        private const int MaxErrorLines = 40;

        /// <summary>The process.</summary>
        private readonly Process _process;

        /// <summary>The last standard error lines.</summary>
        private readonly Queue<string> _errors = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="ChildProcess"/> class.
        /// </summary>
        /// <param name="process">The process, not started yet.</param>
        public ChildProcess(Process process)
        {
            _process = process;
            _process.OutputDataReceived += (_, _) => { };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (_errors)
                {
                    _errors.Enqueue(e.Data);
                    while (_errors.Count > MaxErrorLines)
                    {
                        _errors.Dequeue();
                    }
                }
            };
        }

        /// <inheritdoc />
        public int Id => _process.Id;

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
        public int? ExitCode => HasExited ? _process.ExitCode : null;

        /// <inheritdoc />
        public string ErrorOutput
        {
            get
            {
                lock (_errors)
                {
                    return string.Join(Environment.NewLine, _errors);
                }
            }
        }

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
            catch (InvalidOperationException)
            {
                // The process already exited.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // The process is exiting or cannot be accessed any more.
            }
        }

        /// <inheritdoc />
        public void Dispose() => _process.Dispose();
    }
}
