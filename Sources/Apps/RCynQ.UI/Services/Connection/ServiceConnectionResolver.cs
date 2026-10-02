using RCynQ.Core.Config;
using RCynQ.UI.Services.Configuration;

namespace RCynQ.UI.Services.Connection;

/// <summary>
/// Resolves the configuration file, the service address and the API token from arguments, environment and
/// <c>syncbridge.json</c>.
/// </summary>
/// <remarks>
/// The configuration file is resolved with <see cref="ConfigLocations.Resolve"/> (<c>--config</c>, <c>RCYNQ_CONFIG</c>,
/// <c>./syncbridge.json</c>, then the per-user file). Precedence for the rest: <c>--service</c>/<c>RCYNQ_SERVICE_URL</c>,
/// <c>--token</c>/<c>RCYNQ_API_TOKEN</c>, <c>--token-file</c>/<c>RCYNQ_API_TOKEN_FILE</c>; otherwise the listen port and
/// the token file are derived from the configuration exactly as the service derives them when the UI starts it in the
/// directory of the configuration file.
/// </remarks>
public static class ServiceConnectionResolver
{
    /// <summary>
    /// Resolves the connection settings.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> uses the process environment.</param>
    /// <param name="currentDirectory">The directory searched for <c>syncbridge.json</c>; <see langword="null"/> uses the current directory.</param>
    /// <param name="userConfigPath">The per-user configuration fallback; <see langword="null"/> uses <see cref="ConfigLocations.UserConfigPath"/>.</param>
    /// <returns>The settings.</returns>
    public static ServiceConnectionSettings Resolve(
        IReadOnlyList<string> args,
        Func<string, string?>? environment = null,
        string? currentDirectory = null,
        string? userConfigPath = null)
    {
        ArgumentNullException.ThrowIfNull(args);
        environment ??= Environment.GetEnvironmentVariable;

        var configPath = ConfigLocations.Resolve(Argument(args, "--config"), environment, currentDirectory, userConfigPath);

        // The same reader as the rest of the application: it finds the section whatever the case of its name, as the
        // service does, so the port and the token file are not guessed from defaults for such a file.
        var options = LocalConfigFile.TryLoad(configPath);

        var service = Argument(args, "--service") ?? environment("RCYNQ_SERVICE_URL") ?? $"http://127.0.0.1:{options?.ListenPort ?? 18443}/";
        if (!service.EndsWith('/'))
        {
            service += "/";
        }

        var token = Argument(args, "--token") ?? environment("RCYNQ_API_TOKEN");
        if (token is not null)
        {
            return new ServiceConnectionSettings(new Uri(service), token.Trim(), ApiTokenProvider.ARGUMENT_SOURCE, configPath);
        }

        var tokenFile = Argument(args, "--token-file") ?? environment("RCYNQ_API_TOKEN_FILE") ?? DefaultTokenFile(options, configPath);
        return new ServiceConnectionSettings(new Uri(service), ReadToken(tokenFile), tokenFile, configPath);
    }

    /// <summary>
    /// Reads the token file when it is there and readable.
    /// </summary>
    /// <param name="tokenFile">The token file.</param>
    /// <returns>The token, or an empty string; the token provider reads the file again when a request needs it.</returns>
    private static string ReadToken(string tokenFile)
    {
        try
        {
            return File.Exists(tokenFile) ? File.ReadAllText(tokenFile).Trim() : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The service may be writing the file right now; starting the application must not fail on that.
            return string.Empty;
        }
    }

    /// <summary>
    /// Computes the token file of a node the same way the service does.
    /// </summary>
    /// <param name="options">The node options, or <see langword="null"/>.</param>
    /// <param name="configPath">The configuration file a relative <c>DataRoot</c> is resolved against, or <see langword="null"/> for the current directory.</param>
    /// <returns>The token file path.</returns>
    public static string DefaultTokenFile(SyncBridgeOptions? options, string? configPath = null)
        => AppDataPaths.FromRoot(ConfigLocations.ResolveDataRoot(options ?? new SyncBridgeOptions(), configPath, ConfigLocations.FindPortableRoot())).ApiTokenPath;

    /// <summary>
    /// Reads the value following an option.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static string? Argument(IReadOnlyList<string> args, string name)
    {
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (args[i] == name)
            {
                return args[i + 1];
            }
        }

        return null;
    }
}
