namespace RCynQ.Core.Abstractions.Processes;

/// <summary>
/// A fully specified external process invocation.
/// </summary>
/// <remarks>
/// <see cref="Arguments"/> must already be split into discrete arguments; the
/// process runner is responsible for safe argument passing.
/// </remarks>
public sealed record ProcessSpec
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessSpec"/> record.
    /// </summary>
    /// <param name="fileName">
    /// The executable file name or path. Must not be <see langword="null"/> or blank.
    /// </param>
    /// <param name="arguments">
    /// The discrete command-line arguments. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="workingDirectory">
    /// The working directory for the process, or <see langword="null"/> to use the
    /// current directory. Must not be blank when present.
    /// </param>
    /// <param name="environmentVariables">
    /// Additional environment variables, or <see langword="null"/> for none.
    /// Keys and values must not be blank when present.
    /// </param>
    /// <param name="timeout">
    /// The maximum execution time, or <see langword="null"/> for no timeout.
    /// Must be positive when present.
    /// </param>
    /// <exception cref="ArgumentNullException">A required argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A string argument is blank, or an environment variable is invalid.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is not positive.</exception>
    public ProcessSpec(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables,
        TimeSpan? timeout)
    {
        FileName = ValidateText(fileName, nameof(fileName));
        Arguments = arguments ?? throw new ArgumentNullException(nameof(arguments));
        WorkingDirectory = ValidateOptionalText(workingDirectory, nameof(workingDirectory));
        EnvironmentVariables = ValidateEnvironment(environmentVariables);
        Timeout = ValidateTimeout(timeout);
    }

    /// <summary>
    /// Gets the executable file name or path.
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the discrete command-line arguments.
    /// </summary>
    public IReadOnlyList<string> Arguments { get; }

    /// <summary>
    /// Gets the working directory for the process, or <see langword="null"/> to use
    /// the current directory.
    /// </summary>
    public string? WorkingDirectory { get; }

    /// <summary>
    /// Gets additional environment variables, or <see langword="null"/> for none.
    /// </summary>
    public IReadOnlyDictionary<string, string>? EnvironmentVariables { get; }

    /// <summary>
    /// Gets the maximum execution time, or <see langword="null"/> for no timeout.
    /// </summary>
    public TimeSpan? Timeout { get; }

    /// <summary>
    /// Validates that the given text is not <see langword="null"/> or blank.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="parameterName">The parameter name used in exceptions.</param>
    /// <returns>The trimmed value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    private static string ValidateText(string? value, string parameterName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Value must not be blank.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Validates that the given optional text is not blank when present.
    /// </summary>
    /// <param name="value">The value to validate, or <see langword="null"/>.</param>
    /// <param name="parameterName">The parameter name used in exceptions.</param>
    /// <returns>The trimmed value, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="value"/> is blank.</exception>
    private static string? ValidateOptionalText(string? value, string parameterName)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            throw new ArgumentException("Value must not be blank.", parameterName);
        }

        return trimmed;
    }

    /// <summary>
    /// Validates the environment variable collection.
    /// </summary>
    /// <param name="value">The collection to validate, or <see langword="null"/>.</param>
    /// <returns>The validated collection, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentNullException">A key or value is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A key or value is blank.</exception>
    private static IReadOnlyDictionary<string, string>? ValidateEnvironment(
        IReadOnlyDictionary<string, string>? value)
    {
        if (value is null)
        {
            return null;
        }

        foreach (var (key, environmentValue) in value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Environment variable name must not be blank.", nameof(value));
            }

            if (string.IsNullOrWhiteSpace(environmentValue))
            {
                throw new ArgumentException(
                    $"Environment variable '{key}' must not have a blank value.",
                    nameof(value));
            }
        }

        return value;
    }

    /// <summary>
    /// Validates that the given timeout is positive when present.
    /// </summary>
    /// <param name="value">The timeout to validate, or <see langword="null"/>.</param>
    /// <returns>The validated timeout, or <see langword="null"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not positive.</exception>
    private static TimeSpan? ValidateTimeout(TimeSpan? value)
    {
        if (value is null || value.Value > TimeSpan.Zero)
        {
            return value;
        }

        throw new ArgumentOutOfRangeException(
            nameof(value),
            value,
            "Timeout must be positive when specified.");
    }
}
