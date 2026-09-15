using System.Text.Json;
using System.Text.Json.Nodes;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;

namespace RCynQ.Sync.Configuration;

/// <summary>
/// Reads and edits <c>syncbridge.json</c>.
/// </summary>
/// <remarks>
/// The file holds a <c>SyncBridge</c> section bound to <see cref="SyncBridgeOptions"/>. Edits preserve unknown
/// properties and are written atomically through a temporary file.
/// </remarks>
public static class ConfigurationFileEditor
{
    /// <summary>
    /// The environment variable that points to the configuration file.
    /// </summary>
    public const string EnvironmentVariable = ConfigLocations.EnvironmentVariable;

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
    /// <see cref="EnvironmentVariable"/>, <c>./syncbridge.json</c> when it exists, then the per-user file.
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
            throw new SyncBridgeException($"Configuration file '{path}' does not exist. Create it with 'role set' or pass --config.");
        }

        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var section = root?[SyncBridgeOptions.SectionName]
                ?? throw new SyncBridgeException($"Configuration file '{path}' has no '{SyncBridgeOptions.SectionName}' section.");
            return section.Deserialize<SyncBridgeOptions>(JsonOptions) ?? new SyncBridgeOptions();
        }
        catch (JsonException ex)
        {
            throw new SyncBridgeException($"Configuration file '{path}' is not valid JSON: {ex.Message}", ex);
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
        JsonObject root;
        try
        {
            root = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException ex)
        {
            throw new SyncBridgeException($"Configuration file '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (root[SyncBridgeOptions.SectionName] is not JsonObject section)
        {
            section = new JsonObject();
            root[SyncBridgeOptions.SectionName] = section;
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
    /// Sets the node role and the matching paths. A Mediator has its paths removed.
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
            section[nameof(SyncBridgeOptions.Role)] = role.ToString();
            if (role == NodeRole.Mediator)
            {
                section.Remove(nameof(SyncBridgeOptions.RepoPath));
                section.Remove(nameof(SyncBridgeOptions.WorkdirPath));
                return;
            }

            if (!string.IsNullOrWhiteSpace(repoPath))
            {
                section[nameof(SyncBridgeOptions.RepoPath)] = repoPath;
            }

            if (!string.IsNullOrWhiteSpace(workdirPath))
            {
                section[nameof(SyncBridgeOptions.WorkdirPath)] = workdirPath;
            }
        }, cancellationToken);
    }
}
