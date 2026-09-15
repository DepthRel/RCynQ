using RCynQ.Core.Exceptions;

namespace RCynQ.Core.Abstractions.Processes;

/// <summary>
/// A handle to a long-running external process.
/// </summary>
public interface IRunningProcess : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the process has exited.
    /// </summary>
    bool HasExited { get; }

    /// <summary>
    /// Gets the exit code, or <see langword="null"/> while the process is running.
    /// </summary>
    int? ExitCode { get; }

    /// <summary>
    /// Gets the standard error captured so far.
    /// </summary>
    string StandardError { get; }

    /// <summary>
    /// Waits until the process exits.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    /// <returns>A task that completes when the process has exited.</returns>
    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the process and its child processes. Does nothing when it already exited.
    /// </summary>
    void Kill();
}
