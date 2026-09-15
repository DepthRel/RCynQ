using RCynQ.UI.Services.Hosting;

namespace RCynQ.UI.Tests.Support;

/// <summary>
/// A controllable <see cref="IChildProcess"/>.
/// </summary>
public sealed class FakeChildProcess : IChildProcess
{
    /// <summary>Completes when the process exits.</summary>
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public int Id { get; init; } = 4242;

    /// <inheritdoc />
    public bool HasExited { get; private set; }

    /// <inheritdoc />
    public int? ExitCode { get; private set; }

    /// <inheritdoc />
    public string ErrorOutput { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether <see cref="Kill"/> was called.</summary>
    public bool Killed { get; private set; }

    /// <summary>
    /// Makes the process exit.
    /// </summary>
    /// <param name="exitCode">The exit code.</param>
    public void Exit(int exitCode)
    {
        HasExited = true;
        ExitCode = exitCode;
        _exited.TrySetResult();
    }

    /// <inheritdoc />
    public Task WaitForExitAsync(CancellationToken cancellationToken = default) => _exited.Task.WaitAsync(cancellationToken);

    /// <inheritdoc />
    public void Kill()
    {
        Killed = true;
        Exit(-1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}
