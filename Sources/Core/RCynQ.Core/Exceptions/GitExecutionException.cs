namespace RCynQ.Core.Exceptions;

/// <summary>
/// Thrown when a <c>git</c> execution fails.
/// </summary>
/// <remarks>
/// Git uses exit code <c>1</c> for "differences exist" on some commands, so the
/// exit code is exposed explicitly and the caller decides what is an error.
/// </remarks>
public sealed class GitExecutionException : SyncBridgeException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GitExecutionException"/> class.
    /// </summary>
    public GitExecutionException()
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitExecutionException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public GitExecutionException(string message)
        : base(message)
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitExecutionException"/> class
    /// with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or <see langword="null"/>.
    /// </param>
    public GitExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="GitExecutionException"/> class
    /// with a specified error message and the process exit code.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="exitCode">The exit code reported by the <c>git</c> process.</param>
    public GitExecutionException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the exit code reported by the <c>git</c> process, or <c>-1</c> when
    /// the process did not terminate (for example, it was killed or timed out).
    /// </summary>
    public int ExitCode { get; }
}
