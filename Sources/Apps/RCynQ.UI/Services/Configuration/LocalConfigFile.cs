using System.Text.Json;
using System.Text.Json.Nodes;
using RCynQ.Localization;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// Reads and writes the basic settings of <c>syncbridge.json</c> for the first-run setup of the UI.
/// </summary>
/// <remarks>
/// Unknown properties such as the chain, SSH and transport settings are preserved. The file is written atomically and
/// only when the resulting configuration passes <see cref="SyncBridgeOptionsValidator"/>.
/// </remarks>
public static class LocalConfigFile
{
    /// <summary>JSON options of the configuration file.</summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    /// <summary>JSON document options of the configuration file.</summary>
    private static readonly JsonDocumentOptions DocumentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// The suffix of the copy that is kept of a configuration file that could not be read, before the setup replaces it.
    /// </summary>
    public const string UnreadableBackupSuffix = ".unreadable";

    /// <summary>
    /// Loads the node options, ignoring a missing or malformed file.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>The options, or <see langword="null"/>.</returns>
    public static SyncBridgeOptions? TryLoad(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: DocumentOptions);
            return FindSection(root as JsonObject)?.Deserialize<SyncBridgeOptions>(Json);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Validates the values and writes them into the configuration file.
    /// </summary>
    /// <param name="path">The file path; the file and its directory are created when missing.</param>
    /// <param name="values">The values.</param>
    /// <param name="localizer">The localizer that words the validation problems.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The validation problems; empty when the file was written.</returns>
    /// <exception cref="IOException">The file could not be written.</exception>
    public static async Task<IReadOnlyList<string>> SaveAsync(string path, NodeSetupValues values, ILocalizer localizer, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(localizer);

        JsonObject root;
        var unreadable = false;
        try
        {
            root = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken), documentOptions: DocumentOptions) as JsonObject ?? new JsonObject()
                : new JsonObject();
        }
        catch (JsonException)
        {
            // The setup starts the file anew, which is how a broken file gets repaired from the application.
            root = new JsonObject();
            unreadable = true;
        }

        var section = FindSection(root);
        if (section is null)
        {
            section = new JsonObject();
            root[SyncBridgeOptions.SectionName] = section;
        }

        Set(section, nameof(SyncBridgeOptions.NodeId), values.NodeId.Trim());
        Set(section, nameof(SyncBridgeOptions.Name), values.Name.Trim());
        Set(section, nameof(SyncBridgeOptions.Role), values.Role.ToString());
        Set(section, nameof(SyncBridgeOptions.ListenPort), values.ListenPort);
        Set(section, nameof(SyncBridgeOptions.RepoPath), values.Role == NodeRole.Dest ? values.Path.Trim() : null);
        Set(section, nameof(SyncBridgeOptions.WorkdirPath), values.Role == NodeRole.Source ? values.Path.Trim() : null);
        Set(section, nameof(SyncBridgeOptions.LinkToken), values.Role == NodeRole.Mediator || string.IsNullOrWhiteSpace(values.LinkToken) ? null : values.LinkToken.Trim());

        var endpoint = Nested(section, nameof(SyncBridgeOptions.Endpoint));
        Set(endpoint, nameof(EndpointOptions.Mode), values.EndpointMode.ToString());
        Set(endpoint, nameof(EndpointOptions.BindAddress), values.EndpointBindAddress.Trim());
        Set(endpoint, nameof(EndpointOptions.Port), values.EndpointPort);

        var options = section.Deserialize<SyncBridgeOptions>(Json) ?? new SyncBridgeOptions();
        var errors = SyncBridgeOptionsValidator.Validate(options, localizer);
        if (errors.Count > 0)
        {
            return errors;
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllTextAsync(temp, root.ToJsonString(Json), cancellationToken);
            if (unreadable)
            {
                // The new file carries only what the setup asks for. Everything else the user had written (the links, the
                // SSH settings) is in the file that could not be read, often behind a single typing error: it is kept next
                // to the new one instead of being overwritten for good.
                File.Copy(path, path + UnreadableBackupSuffix, overwrite: true);
            }

            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Finds the <c>SyncBridge</c> section regardless of the key casing.
    /// </summary>
    /// <param name="root">The root object.</param>
    /// <returns>The section, or <see langword="null"/>.</returns>
    private static JsonObject? FindSection(JsonObject? root)
    {
        var key = root?.Select(pair => pair.Key).FirstOrDefault(name => name.Equals(SyncBridgeOptions.SectionName, StringComparison.OrdinalIgnoreCase));
        return key is null ? null : root![key] as JsonObject;
    }

    /// <summary>
    /// Gets a nested object of the section, creating it when it is missing or holds something else.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The nested object, with any settings the user wrote by hand preserved.</returns>
    private static JsonObject Nested(JsonObject section, string name)
    {
        var key = section.Select(pair => pair.Key).FirstOrDefault(existing => existing.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (key is not null && section[key] is JsonObject existingObject)
        {
            return existingObject;
        }

        if (key is not null)
        {
            section.Remove(key);
        }

        var created = new JsonObject();
        section[name] = created;
        return created;
    }

    /// <summary>
    /// Sets or removes one property, replacing a property whose name differs only in case.
    /// </summary>
    /// <param name="section">The object to change.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value, or <see langword="null"/> to remove the property.</param>
    private static void Set(JsonObject section, string name, object? value)
    {
        foreach (var existing in section.Select(pair => pair.Key).Where(key => key.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray())
        {
            section.Remove(existing);
        }

        switch (value)
        {
            case null:
                break;
            case int number:
                section[name] = number;
                break;
            default:
                section[name] = value.ToString();
                break;
        }
    }
}
