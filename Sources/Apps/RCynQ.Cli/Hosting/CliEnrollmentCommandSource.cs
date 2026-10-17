using System.Reflection;
using RCynQ.Sync.Enrollment;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// Names the enrollment command when the command line tool itself writes <c>authorized_keys</c> (<c>rcynq ssh allow</c>,
/// <c>rcynq invite</c>): the running tool, behind <c>dotnet</c> when it runs as an assembly, with <c>enroll --config</c>
/// and the configuration it was given.
/// </summary>
public sealed class CliEnrollmentCommandSource : IEnrollmentCommandSource
{
    /// <summary>The words of the command, or <see langword="null"/> when the running program is unknown.</summary>
    private readonly IReadOnlyList<string>? _command;

    /// <summary>
    /// Initializes a new instance of the <see cref="CliEnrollmentCommandSource"/> class.
    /// </summary>
    /// <param name="configPath">The configuration file of this node.</param>
    public CliEnrollmentCommandSource(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var program = Environment.ProcessPath;
        if (program is null)
        {
            return;
        }

        var words = new List<string> { program };
        if (string.Equals(Path.GetFileNameWithoutExtension(program), "dotnet", StringComparison.OrdinalIgnoreCase) &&
            Assembly.GetEntryAssembly()?.Location is { Length: > 0 } assembly)
        {
            words.Add(assembly);
        }

        words.AddRange(new[] { "enroll", "--config", Path.GetFullPath(configPath) });
        _command = words;
    }

    /// <inheritdoc />
    public IReadOnlyList<string>? GetCommand() => _command;
}
