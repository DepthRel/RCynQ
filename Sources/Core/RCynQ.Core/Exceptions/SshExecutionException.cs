namespace RCynQ.Core.Exceptions;

/// <summary>
/// Thrown when an <c>ssh</c> execution fails.
/// </summary>
public sealed class SshExecutionException : SyncBridgeException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshExecutionException"/> class.
    /// </summary>
    public SshExecutionException()
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SshExecutionException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    public SshExecutionException(string message)
        : base(message)
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SshExecutionException"/> class
    /// with a specified error message and a reference to the inner exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">
    /// The exception that is the cause of the current exception, or <see langword="null"/>.
    /// </param>
    public SshExecutionException(string message, Exception innerException)
        : base(message, innerException)
    {
        ExitCode = -1;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SshExecutionException"/> class
    /// with a specified error message and the process exit code.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="exitCode">The exit code reported by the <c>ssh</c> process.</param>
    public SshExecutionException(string message, int exitCode)
        : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// Gets the exit code reported by the <c>ssh</c> process, or <c>-1</c> when
    /// the process did not terminate (for example, it was killed or timed out).
    /// </summary>
    public int ExitCode { get; }
}
