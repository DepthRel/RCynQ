using System.Text.Json;
using System.Text.Json.Nodes;
using RCynQ.Core.Config;

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
        var options = TryLoadOptions(configPath);

        var service = Argument(args, "--service") ?? environment("RCYNQ_SERVICE_URL") ?? $"http://127.0.0.1:{options?.ListenPort ?? 18443}/";
        if (!service.EndsWith('/'))
        {
            service += "/";
        }

        var token = Argument(args, "--token") ?? environment("RCYNQ_API_TOKEN");
        if (token is not null)
        {
            return new ServiceConnectionSettings(new Uri(service), token.Trim(), ApiTokenProvider.ArgumentSource, configPath);
        }

        var tokenFile = Argument(args, "--token-file") ?? environment("RCYNQ_API_TOKEN_FILE") ?? DefaultTokenFile(options, configPath);
        var fileToken = File.Exists(tokenFile) ? File.ReadAllText(tokenFile).Trim() : string.Empty;
        return new ServiceConnectionSettings(new Uri(service), fileToken, tokenFile, configPath);
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
    /// Loads options from a configuration file, ignoring missing or malformed files.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The options, or <see langword="null"/>.</returns>
    private static SyncBridgeOptions? TryLoadOptions(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            return root?[SyncBridgeOptions.SectionName]?.Deserialize<SyncBridgeOptions>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

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
