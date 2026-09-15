namespace RCynQ.Core.Abstractions.Ssh;

/// <summary>
/// The captured result of a remote command executed over SSH.
/// </summary>
public sealed record SshResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SshResult"/> record.
    /// </summary>
    /// <param name="exitCode">The exit code of the remote command.</param>
    /// <param name="standardOutput">
    /// The captured standard output. Must not be <see langword="null"/>;
    /// use an empty string when nothing was produced.
    /// </param>
    /// <param name="standardError">
    /// The captured standard error. Must not be <see langword="null"/>;
    /// use an empty string when nothing was produced.
    /// </param>
    /// <exception cref="ArgumentNullException">A stream argument is <see langword="null"/>.</exception>
    public SshResult(int exitCode, string standardOutput, string standardError)
    {
        ExitCode = exitCode;
        StandardOutput = standardOutput ?? throw new ArgumentNullException(nameof(standardOutput));
        StandardError = standardError ?? throw new ArgumentNullException(nameof(standardError));
    }

    /// <summary>
    /// Gets the exit code of the remote command.
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

    /// <summary>
    /// Gets a value indicating whether the remote command exited with code <c>0</c>.
    /// </summary>
    public bool Succeeded => ExitCode == 0;
}
