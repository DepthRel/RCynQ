using System.Text.Json;
using System.Text.Json.Nodes;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// <see cref="IUiSettingsStore"/> backed by a small JSON file (<c>ui-settings.json</c>) that lives next to the
/// configuration file of the node, so nothing is stored outside the application folder of a portable installation.
/// </summary>
/// <remarks>
/// A missing or damaged file reads as empty, and the next write replaces it; the file is written atomically.
/// </remarks>
public sealed class JsonUiSettingsStore : IUiSettingsStore
{
    /// <summary>
    /// The name of the file next to the configuration file.
    /// </summary>
    public const string FILE_NAME = "ui-settings.json";

    /// <summary>
    /// The JSON options of the file.
    /// </summary>
    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    /// <summary>
    /// Serializes the writes of one process.
    /// </summary>
    private readonly object _gate = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonUiSettingsStore"/> class.
    /// </summary>
    /// <param name="path">The path of the settings file; its directory is created on the first write.</param>
    public JsonUiSettingsStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
    }

    /// <summary>
    /// Gets the full path of the settings file.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the path of the settings file that belongs to a configuration file: the same directory.
    /// </summary>
    /// <param name="configPath">The path of <c>syncbridge.json</c>.</param>
    /// <returns>The path of <c>ui-settings.json</c>.</returns>
    public static string PathFor(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(configPath))!, FILE_NAME);
    }

    /// <inheritdoc />
    public string? Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        var root = ReadRoot();
        return root.TryGetPropertyValue(key, out var node) && node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <inheritdoc />
    public void Set(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        lock (_gate)
        {
            var root = ReadRoot();
            root[key] = value;

            var directory = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(directory);
            var temp = Path.Combine(directory, $".{FILE_NAME}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, root.ToJsonString(_json));
                File.Move(temp, FilePath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }
    }

    /// <summary>
    /// Reads the file as a JSON object.
    /// </summary>
    /// <returns>The object; empty when the file is missing, unreadable or not an object.</returns>
    private JsonObject ReadRoot()
    {
        try
        {
            return File.Exists(FilePath) && JsonNode.Parse(File.ReadAllText(FilePath)) is JsonObject root ? root : new JsonObject();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new JsonObject();
        }
    }
}
