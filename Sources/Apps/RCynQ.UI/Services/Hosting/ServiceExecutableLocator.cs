namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Finds RCynQ.Service for the UI.
/// </summary>
/// <remarks>
/// Search order: <c>--service-exe</c> or <c>RCYNQ_SERVICE_EXE</c>; the published layout (<c>ui/../service/</c>);
/// the UI directory itself; the development layout (<c>Apps/RCynQ.UI/bin/&lt;cfg&gt;/&lt;tfm&gt;</c> next to
/// <c>Apps/RCynQ.Service/bin/&lt;cfg&gt;/&lt;tfm&gt;</c>).
/// </remarks>
public static class ServiceExecutableLocator
{
    /// <summary>The command-line option with an explicit service executable or assembly.</summary>
    public const string ARGUMENT = "--service-exe";

    /// <summary>The environment variable with an explicit service executable or assembly.</summary>
    public const string ENVIRONMENT_VARIABLE = "RCYNQ_SERVICE_EXE";

    /// <summary>The service assembly name.</summary>
    public const string ASSEMBLY_NAME = "RCynQ.Service";

    /// <summary>
    /// Locates the service.
    /// </summary>
    /// <param name="baseDirectory">The UI application directory.</param>
    /// <param name="args">The UI command-line arguments.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <param name="isWindows">Whether executables carry the <c>.exe</c> extension.</param>
    /// <returns>The launch command, or <see langword="null"/> when the service was not found.</returns>
    public static ServiceLaunchCommand? Locate(string baseDirectory, IReadOnlyList<string> args, Func<string, string?> environment, bool isWindows)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(environment);

        var explicitPath = ArgumentValue(args) ?? environment(ENVIRONMENT_VARIABLE);
        return !string.IsNullOrWhiteSpace(explicitPath)
            ? ExecutableLocator.FromFile(Path.GetFullPath(explicitPath), environment)
            : ExecutableLocator.Locate(baseDirectory, ASSEMBLY_NAME, "service", ASSEMBLY_NAME, isWindows, environment);
    }

    /// <summary>
    /// Reads the value of <see cref="ARGUMENT"/>.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static string? ArgumentValue(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == ARGUMENT)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
