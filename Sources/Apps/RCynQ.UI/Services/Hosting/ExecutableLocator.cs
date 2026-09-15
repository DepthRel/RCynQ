namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Finds a sibling RCynQ application (service or CLI) of the UI in the published or the development layout.
/// </summary>
public static class ExecutableLocator
{
    /// <summary>
    /// Locates an application.
    /// </summary>
    /// <param name="baseDirectory">The UI application directory.</param>
    /// <param name="assemblyName">The assembly name, e.g. <c>RCynQ.Service</c> or <c>rcynq</c>.</param>
    /// <param name="publishedDirectory">The directory of the application next to <c>ui</c> in a published layout.</param>
    /// <param name="projectDirectory">The project directory next to <c>RCynQ.UI</c> in the source tree.</param>
    /// <param name="isWindows">Whether executables carry the <c>.exe</c> extension.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The launch command, or <see langword="null"/> when the application was not found.</returns>
    public static ServiceLaunchCommand? Locate(string baseDirectory, string assemblyName, string publishedDirectory, string projectDirectory, bool isWindows, Func<string, string?> environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyName);
        ArgumentNullException.ThrowIfNull(environment);

        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory));
        var exeName = isWindows ? assemblyName + ".exe" : assemblyName;
        foreach (var candidate in new[] { Path.Combine(directory, "..", publishedDirectory, exeName), Path.Combine(directory, exeName) })
        {
            if (File.Exists(candidate))
            {
                return new ServiceLaunchCommand(Path.GetFullPath(candidate), Array.Empty<string>());
            }
        }

        var framework = new DirectoryInfo(directory);
        var configuration = framework.Parent;
        var bin = configuration?.Parent;
        var project = bin?.Parent;
        if (project?.Parent is null || !string.Equals(bin!.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var output = Path.Combine(project.Parent.FullName, projectDirectory, "bin", configuration!.Name, framework.Name);
        var exe = Path.Combine(output, exeName);
        if (File.Exists(exe))
        {
            return new ServiceLaunchCommand(exe, Array.Empty<string>());
        }

        var dll = Path.Combine(output, assemblyName + ".dll");
        return File.Exists(dll) ? new ServiceLaunchCommand(DotnetHost(environment), new[] { dll }) : null;
    }

    /// <summary>
    /// Builds a launch command for an explicit executable or assembly.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The command, or <see langword="null"/> when the file does not exist.</returns>
    public static ServiceLaunchCommand? FromFile(string path, Func<string, string?> environment)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? new ServiceLaunchCommand(DotnetHost(environment), new[] { path })
            : new ServiceLaunchCommand(path, Array.Empty<string>());
    }

    /// <summary>
    /// Gets the <c>dotnet</c> host used to run a framework-dependent assembly.
    /// </summary>
    /// <param name="environment">Reads an environment variable.</param>
    /// <returns>The host path or <c>dotnet</c>.</returns>
    public static string DotnetHost(Func<string, string?> environment)
        => environment("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host) ? host : "dotnet";
}
