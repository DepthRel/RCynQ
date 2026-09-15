namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// A child process started by the UI.
/// </summary>
public interface IChildProcess : IDisposable
{
    /// <summary>Gets the process identifier.</summary>
    int Id { get; }

    /// <summary>Gets a value indicating whether the process exited.</summary>
    bool HasExited { get; }

    /// <summary>Gets the exit code, or <see langword="null"/> while the process runs.</summary>
    int? ExitCode { get; }

    /// <summary>Gets the last lines the process wrote to standard error.</summary>
    string ErrorOutput { get; }

    /// <summary>
    /// Waits until the process exits and its output streams are drained.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task that completes when the process exited.</returns>
    Task WaitForExitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Kills the process and its descendants.
    /// </summary>
    void Kill();
}
