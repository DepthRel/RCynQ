using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Processes;

/// <summary>
/// Executes external processes.
/// </summary>
/// <remarks>
/// Implementations must pass arguments as discrete values without shell interpolation and must honor the
/// working directory, environment and timeout of the <see cref="ProcessSpec"/>.
/// </remarks>
public interface IProcessRunner
{
    /// <summary>
    /// Executes a process to completion and captures its output.
    /// </summary>
    /// <param name="spec">The process specification.</param>
    /// <param name="cancellationToken">A token to cancel the execution; the process is killed on cancellation.</param>
    /// <returns>The captured result. A non-zero exit code is not an error at this level.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="SyncBridgeException">The process could not be started or timed out.</exception>
    Task<ProcessResult> RunAsync(ProcessSpec spec, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a long-running process (for example an SSH port forward) without waiting for it to exit.
    /// </summary>
    /// <param name="spec">The process specification. The timeout is ignored for long-running processes.</param>
    /// <returns>A handle to the running process.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="spec"/> is <see langword="null"/>.</exception>
    /// <exception cref="SyncBridgeException">The process could not be started.</exception>
    IRunningProcess Start(ProcessSpec spec);
}
