namespace RCynQ.Core.Abstractions.Processes;

/// <summary>
/// The captured result of an external process execution.
/// </summary>
public sealed record ProcessResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessResult"/> record.
    /// </summary>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="standardOutput">
    /// The captured standard output. Must not be <see langword="null"/>;
    /// use an empty string when nothing was produced.
    /// </param>
    /// <param name="standardError">
    /// The captured standard error. Must not be <see langword="null"/>;
    /// use an empty string when nothing was produced.
    /// </param>
    /// <exception cref="ArgumentNullException">A stream argument is <see langword="null"/>.</exception>
    public ProcessResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    }

    /// <summary>
    /// Gets the process exit code.
    /// </summary>
    public int ExitCode { get; }

    /// <summary>
    /// Gets the captured standard output.
    /// </summary>
    public string StandardOutput { get; }

    /// <summary>
    /// Gets the captured standard error.
    /// </summary>
    public string StandardError { get; }
}
