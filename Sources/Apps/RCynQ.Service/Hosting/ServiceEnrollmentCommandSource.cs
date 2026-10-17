using RCynQ.Sync.Enrollment;

namespace RCynQ.Service.Hosting;

/// <summary>
/// Names the enrollment command for the SSH server of this node: the command line tool the application passed with
/// <see cref="CLI_ARGUMENT"/> (and <see cref="CLI_PREFIX_ARGUMENT"/>), or the <c>rcynq</c> of the installation found next
/// to the service, followed by <c>enroll --config</c> and the configuration of this node.
/// </summary>
public sealed class ServiceEnrollmentCommandSource : IEnrollmentCommandSource
{
    /// <summary>The argument with the program of the command line tool.</summary>
    public const string CLI_ARGUMENT = "--cli";

    /// <summary>The argument with a word that comes before the commands of the tool (the assembly behind <c>dotnet</c>); may repeat.</summary>
    public const string CLI_PREFIX_ARGUMENT = "--cli-arg";

    /// <summary>The name of the command line tool.</summary>
    private const string CLI_NAME = "rcynq";

    /// <summary>The words of the command, or <see langword="null"/> when the tool was not found.</summary>
    private readonly IReadOnlyList<string>? _command;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceEnrollmentCommandSource"/> class.
    /// </summary>
    /// <param name="args">The command-line arguments of the service.</param>
    /// <param name="configPath">The configuration file of this node.</param>
    /// <param name="baseDirectory">The folder of the service, where an installation keeps the tool beside it.</param>
    public ServiceEnrollmentCommandSource(IReadOnlyList<string> args, string configPath, string baseDirectory)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var program = Value(args, CLI_ARGUMENT) ?? Locate(baseDirectory);
        _command = program is null
            ? null
            : new[] { Path.GetFullPath(program) }
                .Concat(Values(args, CLI_PREFIX_ARGUMENT))
                .Concat(new[] { "enroll", "--config", Path.GetFullPath(configPath) })
                .ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? GetCommand() => _command;

    /// <summary>
    /// Finds the tool of an installation: in the <c>cli</c> folder next to the folder of the service, or beside it.
    /// </summary>
    /// <param name="baseDirectory">The folder of the service.</param>
    /// <returns>The path of the tool, or <see langword="null"/>.</returns>
    private static string? Locate(string baseDirectory)
    {
        var name = OperatingSystem.IsWindows() ? CLI_NAME + ".exe" : CLI_NAME;
        return new[] { Path.Combine(baseDirectory, "..", "cli", name), Path.Combine(baseDirectory, name) }.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Reads the value after the first occurrence of an argument.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="name">The argument.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static string? Value(IReadOnlyList<string> args, string name) => Values(args, name).FirstOrDefault();

    /// <summary>
    /// Reads the values after every occurrence of an argument.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="name">The argument.</param>
    /// <returns>The values, in order.</returns>
    private static IEnumerable<string> Values(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == name)
            {
                yield return args[i + 1];
            }
        }
    }
}
