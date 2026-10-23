using System.Text.Json;
using System.Text.Json.Nodes;
using RCynQ.Core.Config;
using RCynQ.Core.Config.Legacy;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Localization;

namespace RCynQ.Sync.Configuration;

/// <summary>
/// Reads and edits <c>syncbridge.json</c>.
/// </summary>
/// <remarks>
/// The file holds a <c>SyncBridge</c> section bound to <see cref="SyncBridgeOptions"/>. Edits preserve unknown
/// properties and are written atomically through a temporary file. The section and its properties are found whatever
/// the case of their names, as the service binds them.
/// </remarks>
public static class ConfigurationFileEditor
{
    /// <summary>
    /// The environment variable that points to the configuration file.
    /// </summary>
    public const string ENVIRONMENT_VARIABLE = ConfigLocations.ENVIRONMENT_VARIABLE;

    /// <summary>
    /// JSON options used for reading and writing the file.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    /// <summary>
    /// Resolves the configuration file path with <see cref="ConfigLocations.Resolve"/>: explicit path,
    /// <see cref="ENVIRONMENT_VARIABLE"/>, <c>./syncbridge.json</c> when it exists, then the per-user file.
    /// </summary>
    /// <param name="explicitPath">The explicitly requested path, or <see langword="null"/>.</param>
    /// <returns>The full path.</returns>
    public static string ResolvePath(string? explicitPath) => ConfigLocations.Resolve(explicitPath);

    /// <summary>
    /// Loads the options from a configuration file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The options.</returns>
    /// <exception cref="SyncBridgeException">The file does not exist or is malformed.</exception>
    public static async Task<SyncBridgeOptions> LoadAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
        {
            throw LocalizedError.Create(message => new SyncBridgeException(message), "ConfigFile.Missing", path);
        }

        ConfigurationFileMigrator.Migrate(path);

        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var section = FindSection(root as JsonObject)?.Section
                ?? throw LocalizedError.Create(message => new SyncBridgeException(message), "ConfigFile.NoSection", path, SyncBridgeOptions.SECTION_NAME);
            return section.Deserialize<SyncBridgeOptions>(JsonOptions) ?? new SyncBridgeOptions();
        }
        catch (JsonException ex)
        {
            throw LocalizedError.Create(message => new SyncBridgeException(message, ex), "ConfigFile.BadJson", path, ex);
        }
    }

    /// <summary>
    /// Updates the <c>SyncBridge</c> section, creating the file when it does not exist.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="update">The update applied to the section object.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="SyncBridgeException">The existing file is malformed.</exception>
    public static async Task UpdateAsync(string path, Action<JsonObject> update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        // An edit is made to the current format, never next to the properties of an earlier version.
        ConfigurationFileMigrator.Migrate(path);
        JsonObject root;
        try
        {
            root = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            throw LocalizedError.Create(message => new SyncBridgeException(message, ex), "ConfigFile.BadJson", path, ex);
        }

        var found = FindSection(root);
        if (found?.Section is not JsonObject section)
        {
            section = new JsonObject();
            root[found?.Key ?? SyncBridgeOptions.SECTION_NAME] = section;
        }

        update(section);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(JsonOptions), cancellationToken);
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>
    /// Sets the node role and the matching paths. A Relay has its paths removed.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <param name="role">The role.</param>
    /// <param name="repoPath">The repository path, or <see langword="null"/> to keep the current value.</param>
    /// <param name="workdirPath">The working directory, or <see langword="null"/> to keep the current value.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="ArgumentException"><paramref name="role"/> is not defined.</exception>
    public static Task SetRoleAsync(string path, NodeRole role, string? repoPath, string? workdirPath, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentException($"Node role '{role}' is not defined.", nameof(role));
        }

        return UpdateAsync(path, section =>
        {
            SetProperty(section, nameof(SyncBridgeOptions.Role), role.ToString());
            RepositoryConfigurationEditor.Apply(section, role, role == NodeRole.Origin ? repoPath : workdirPath);
        }, cancellationToken);
    }

    /// <summary>
    /// Sets or removes a property that holds an object or an array, matching its name without regard to case.
    /// </summary>
    /// <param name="node">The object that holds the property.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the property.</param>
    public static void SetNode(JsonObject node, string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        foreach (var key in node.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            node.Remove(key);
        }

        if (value is not null)
        {
            node[name] = value;
        }
    }

    /// <summary>
    /// Sets or removes a property of a configuration object, whatever the case its name is written in.
    /// </summary>
    /// <param name="node">The configuration object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The new value, or <see langword="null"/> to remove the property.</param>
    /// <remarks>
    /// The service binds the file without regard to the case of the names, so <c>"role"</c> and <c>"Role"</c> are the same
    /// setting for it. An edit that only knew the usual spelling would add a second property next to the one the user
    /// wrote, and the service refuses a file with the same setting twice. The spelling found in the file is kept.
    /// </remarks>
    public static void SetProperty(JsonObject node, string name, string? value)
        => SetValue(node, name, value is null ? null : JsonValue.Create(value));

    /// <summary>
    /// Sets a switch of a configuration object, whatever the case its name is written in.
    /// </summary>
    /// <param name="node">The configuration object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The new value.</param>
    public static void SetProperty(JsonObject node, string name, bool value) => SetValue(node, name, JsonValue.Create(value));

    /// <summary>
    /// Sets or removes a property of a configuration object, keeping the spelling of its name found in the file.
    /// </summary>
    /// <param name="node">The configuration object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The new value, or <see langword="null"/> to remove the property.</param>
    private static void SetValue(JsonObject node, string name, JsonNode? value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var existing = node.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        foreach (var key in existing.Skip(1))
        {
            node.Remove(key);
        }

        var target = existing.Length > 0 ? existing[0] : name;
        if (value is null)
        {
            node.Remove(target);
        }
        else
        {
            node[target] = value;
        }
    }

    /// <summary>
    /// Finds the <c>SyncBridge</c> section of a configuration document, whatever the case its name is written in.
    /// </summary>
    /// <param name="root">The root object of the document, or <see langword="null"/>.</param>
    /// <returns>The name the section is written with and the section, or <see langword="null"/> when there is none.</returns>
    private static (string Key, JsonNode? Section)? FindSection(JsonObject? root)
    {
        var key = root?.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(SyncBridgeOptions.SECTION_NAME, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : (key, root![key]);
    }
}
