namespace RCynQ.Core.Config;

/// <summary>
/// Resolves where the configuration and the data of a node live, the same way for the service, the CLI and the UI.
/// </summary>
/// <remarks>
/// <para>
/// A <b>portable installation</b> is a directory that contains the <see cref="PORTABLE_MARKER_FILE_NAME"/> file (created by
/// the publish scripts) next to the <c>service</c>, <c>ui</c> and <c>cli</c> directories. In a portable installation the
/// configuration is <c>&lt;root&gt;/config/syncbridge.json</c> and all data (database, keys, logs, temporary files) is
/// stored below <c>&lt;root&gt;/data</c>, so nothing is written to the user profile.
/// </para>
/// <para>
/// Configuration precedence: an explicit path, the <see cref="ENVIRONMENT_VARIABLE"/> variable, the portable
/// configuration, <c>./syncbridge.json</c> when it exists, and finally <see cref="UserConfigPath"/>.
/// </para>
/// </remarks>
public static class ConfigLocations
{
    /// <summary>
    /// The environment variable that points to the configuration file.
    /// </summary>
    public const string ENVIRONMENT_VARIABLE = "RCYNQ_CONFIG";

    /// <summary>
    /// The file that marks the root of a portable installation.
    /// </summary>
    public const string PORTABLE_MARKER_FILE_NAME = "rcynq.portable";

    /// <summary>
    /// The configuration directory inside a portable installation.
    /// </summary>
    public const string PORTABLE_CONFIG_DIRECTORY = "config";

    /// <summary>
    /// The data directory inside a portable installation.
    /// </summary>
    public const string PORTABLE_DATA_DIRECTORY = "data";

    /// <summary>
    /// Gets the per-user configuration file used outside portable installations: <c>%APPDATA%\RCynQ\syncbridge.json</c>
    /// on Windows, <c>~/.config/RCynQ/syncbridge.json</c> on Linux and the platform equivalent on macOS.
    /// </summary>
    public static string UserConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "RCynQ",
        SyncBridgeOptions.DEFAULT_FILE_NAME);

    /// <summary>
    /// Finds the portable installation that contains an application directory.
    /// </summary>
    /// <param name="baseDirectory">The application directory; <see langword="null"/> uses <see cref="AppContext.BaseDirectory"/>.</param>
    /// <returns>The installation root (the application directory or its parent holding the marker), or <see langword="null"/>.</returns>
    public static string? FindPortableRoot(string? baseDirectory = null)
    {
        var directory = new DirectoryInfo(Path.TrimEndingDirectorySeparator(Path.GetFullPath(baseDirectory ?? AppContext.BaseDirectory)));
        for (var level = 0; level < 2 && directory is not null; level++, directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, PORTABLE_MARKER_FILE_NAME)))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves the configuration file path.
    /// </summary>
    /// <param name="explicitPath">The explicitly requested path, or <see langword="null"/>.</param>
    /// <param name="environment">Reads an environment variable; <see langword="null"/> uses the process environment.</param>
    /// <param name="currentDirectory">The directory searched for <c>syncbridge.json</c>; <see langword="null"/> uses the current directory.</param>
    /// <param name="userConfigPath">The per-user fallback; <see langword="null"/> uses <see cref="UserConfigPath"/>.</param>
    /// <param name="portableRoot">Returns the portable installation root; <see langword="null"/> detects it with <see cref="FindPortableRoot"/>.</param>
    /// <returns>The full path; the file may not exist yet.</returns>
    public static string Resolve(
        string? explicitPath,
        Func<string, string?>? environment = null,
        string? currentDirectory = null,
        string? userConfigPath = null,
        Func<string?>? portableRoot = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            return Path.GetFullPath(explicitPath);
        }

        var fromEnvironment = (environment ?? Environment.GetEnvironmentVariable)(ENVIRONMENT_VARIABLE);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        var root = (portableRoot ?? (() => FindPortableRoot()))();
        if (root is not null)
        {
            return Path.Combine(root, PORTABLE_CONFIG_DIRECTORY, SyncBridgeOptions.DEFAULT_FILE_NAME);
        }

        var local = Path.Combine(currentDirectory ?? Directory.GetCurrentDirectory(), SyncBridgeOptions.DEFAULT_FILE_NAME);
        return File.Exists(local) ? Path.GetFullPath(local) : Path.GetFullPath(userConfigPath ?? UserConfigPath);
    }

    /// <summary>
    /// Resolves the data directory of a node.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="configPath">The configuration file a relative <c>DataRoot</c> is resolved against, or <see langword="null"/> for the current directory.</param>
    /// <param name="portableRoot">The portable installation root, or <see langword="null"/>.</param>
    /// <returns>
    /// The full path: <c>DataRoot</c> when configured; otherwise <c>&lt;portable root&gt;/data</c>; otherwise
    /// <c>%LOCALAPPDATA%/RCynQ/&lt;NodeId&gt;</c>.
    /// </returns>
    public static string ResolveDataRoot(SyncBridgeOptions options, string? configPath, string? portableRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.IsNullOrWhiteSpace(options.DataRoot))
        {
            var dataRoot = options.DataRoot.Trim();
            if (Path.IsPathRooted(dataRoot))
            {
                return Path.GetFullPath(dataRoot);
            }

            var baseDirectory = configPath is null ? Directory.GetCurrentDirectory() : Path.GetDirectoryName(Path.GetFullPath(configPath))!;
            return Path.GetFullPath(Path.Combine(baseDirectory, dataRoot));
        }

        if (portableRoot is not null)
        {
            return Path.Combine(portableRoot, PORTABLE_DATA_DIRECTORY);
        }

        var nodeSegment = string.IsNullOrWhiteSpace(options.NodeId) ? "default" : options.NodeId.Trim();
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
            "RCynQ",
            nodeSegment);
    }

    /// <summary>
    /// Makes the storage settings absolute so every component uses the same files: resolves <c>DataRoot</c> and points
    /// the SSH <c>known_hosts</c> file and the SSH client configuration into the data directory unless they were configured.
    /// </summary>
    /// <param name="options">The node options to update.</param>
    /// <param name="configPath">The configuration file, or <see langword="null"/>.</param>
    /// <param name="portableRoot">The portable installation root, or <see langword="null"/>.</param>
    public static void ApplyStorageDefaults(SyncBridgeOptions options, string? configPath, string? portableRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.DataRoot = ResolveDataRoot(options, configPath, portableRoot);
        if (string.IsNullOrWhiteSpace(options.Ssh.KnownHostsFile))
        {
            options.Ssh.KnownHostsFile = AppDataPaths.FromRoot(options.DataRoot).KnownHostsPath;
        }

        if (string.IsNullOrWhiteSpace(options.Ssh.ConfigFile))
        {
            options.Ssh.ConfigFile = AppDataPaths.FromRoot(options.DataRoot).SshConfigPath;
        }
    }
}
