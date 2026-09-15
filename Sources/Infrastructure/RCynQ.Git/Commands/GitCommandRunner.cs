using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.Git.Commands;

/// <summary>
/// Default <see cref="IGitCommandRunner"/>.
/// </summary>
/// <remarks>
/// Every command runs with <c>core.autocrlf=false</c> and <c>core.safecrlf=false</c> so diffs and applies are byte exact,
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
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var env = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_TERMINAL_PROMPT"] = "0",
            ["GIT_ALLOW_PROTOCOL"] = "ssh:file"
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
            ConfigPrefix.Concat(arguments).ToArray(),
            workingDirectory,
            env,
            _options.CommandTimeout);
        return _processRunner.RunAsync(spec, cancellationToken);
    }
}
