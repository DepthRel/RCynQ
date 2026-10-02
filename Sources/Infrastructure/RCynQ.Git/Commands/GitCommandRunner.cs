using RCynQ.Core.Abstractions.Processes;

namespace RCynQ.Git.Commands;

/// <summary>
/// Default <see cref="IGitCommandRunner"/>.
/// </summary>
/// <remarks>
/// <see cref="RunAsync"/> runs with <c>core.autocrlf=false</c> and <c>core.safecrlf=false</c> so diffs and applies are byte exact;
/// <see cref="RunInWorkingTreeAsync"/> keeps the line-ending settings of the repository for commands that touch the user's files.
/// Every command runs
/// with <c>GIT_TERMINAL_PROMPT=0</c> so git never blocks on credentials, and with <c>GIT_ALLOW_PROTOCOL=ssh:file</c> so
/// dangerous transports such as <c>ext::</c> can never be used.
/// </remarks>
public sealed class GitCommandRunner : IGitCommandRunner
{
    /// <summary>
    /// Configuration overrides passed before every command.
    /// </summary>
    public static readonly IReadOnlyList<string> ConfigPrefix = new[]
    {
        "-c", "core.autocrlf=false",
        "-c", "core.safecrlf=false",
        "-c", "core.quotepath=false",
        "-c", "color.ui=never"
    };

    /// <summary>
    /// Configuration overrides passed before commands that touch the working tree: no line-ending override, so the files come
    /// out exactly as the user's own Git writes and reads them.
    /// </summary>
    public static readonly IReadOnlyList<string> WorkingTreeConfigPrefix = new[]
    {
        "-c", "core.quotepath=false",
        "-c", "color.ui=never"
    };

    /// <summary>
    /// The process runner.
    /// </summary>
    private readonly IProcessRunner _processRunner;

    /// <summary>
    /// The git options.
    /// </summary>
    private readonly GitOptions _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GitCommandRunner"/> class.
    /// </summary>
    /// <param name="processRunner">The process runner.</param>
    /// <param name="options">The git options.</param>
    public GitCommandRunner(IProcessRunner processRunner, GitOptions options)
    {
        _processRunner = processRunner;
        _options = options;
    }

    /// <inheritdoc />
    public Task<ProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
        => RunWithPrefixAsync(ConfigPrefix, arguments, workingDirectory, environment, cancellationToken);

    /// <inheritdoc />
    public Task<ProcessResult> RunInWorkingTreeAsync(
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        CancellationToken cancellationToken = default)
        => RunWithPrefixAsync(WorkingTreeConfigPrefix, arguments, workingDirectory, environment, cancellationToken);

    /// <summary>
    /// Runs git with the given configuration overrides.
    /// </summary>
    /// <param name="prefix">The configuration overrides.</param>
    /// <param name="arguments">The git arguments.</param>
    /// <param name="workingDirectory">The working directory, or <see langword="null"/>.</param>
    /// <param name="environment">Additional environment variables, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>The captured result.</returns>
    private Task<ProcessResult> RunWithPrefixAsync(
        IReadOnlyList<string> prefix,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ALLOW_PROTOCOL"] = "ssh:file",

            // Reading commands (status, diff) otherwise refresh the cached file information in .git/index. With this,
            // a command that only reads writes nothing into the repository; the repository of an Origin is never changed.
            ["GIT_OPTIONAL_LOCKS"] = "0"
        };

        if (environment is not null)
        {
            foreach (var (key, value) in environment)
            {
                env[key] = value;
            }
        }

        var spec = new ProcessSpec(
            _options.GitExecutable,
            prefix.Concat(arguments).ToArray(),
            workingDirectory,
            env,
            _options.CommandTimeout);
        return _processRunner.RunAsync(spec, cancellationToken);
    }
}
