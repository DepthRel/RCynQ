using System.Text.Json.Nodes;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Config;

/// <summary>
/// Edits the repositories of a node in the JSON of its configuration file.
/// </summary>
/// <remarks>
/// Used by everything that writes the file without a running service: the first-run setup, the role settings and the
/// command line. Property names are matched without regard to case, the way the configuration is read.
/// </remarks>
public static class RepositoryConfigurationEditor
{
    /// <summary>
    /// Sets the role-dependent part of the repositories: a Relay loses them, and a path given for another role becomes
    /// the path of the repository the node works with (the first repository when the node has none yet).
    /// </summary>
    /// <param name="section">The configuration section of the node.</param>
    /// <param name="role">The role the node gets.</param>
    /// <param name="path">The path of the repository the node works with, or <see langword="null"/> to keep it.</param>
    public static void Apply(JsonObject section, NodeRole role, string? path)
    {
        ArgumentNullException.ThrowIfNull(section);
        var repositoriesKey = FindKey(section, nameof(SyncBridgeOptions.Repositories)) ?? nameof(SyncBridgeOptions.Repositories);
        var activeKey = FindKey(section, nameof(SyncBridgeOptions.ActiveRepository)) ?? nameof(SyncBridgeOptions.ActiveRepository);
        if (role == NodeRole.Relay)
        {
            section.Remove(repositoriesKey);
            section.Remove(activeKey);
            return;
        }

        if (section[repositoriesKey] is not JsonArray repositories)
        {
            repositories = new JsonArray();
            section[repositoriesKey] = repositories;
        }

        var entries = repositories.OfType<JsonObject>().ToArray();
        var activeName = Text(section[activeKey]);
        var active = entries.FirstOrDefault(entry => activeName is not null && string.Equals(Text(Value(entry, nameof(RepositoryOptions.Name))), activeName, StringComparison.OrdinalIgnoreCase))
            ?? entries.FirstOrDefault();
        path = string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        if (path is not null)
        {
            if (active is null)
            {
                active = new JsonObject
                {
                    [nameof(RepositoryOptions.Id)] = RepositoryEntry.DefaultId.ToString(),
                    [nameof(RepositoryOptions.Name)] = string.Empty,
                    [nameof(RepositoryOptions.Path)] = path
                };
                repositories.Add(active);
                entries = new[] { active };
            }
            else
            {
                Set(active, nameof(RepositoryOptions.Path), path);
            }
        }

        if (role != NodeRole.Origin)
        {
            return;
        }

        // An Origin tells its repositories apart by their names, so every one of them gets a name: that of its folder.
        var taken = new HashSet<string>(entries.Select(entry => Text(Value(entry, nameof(RepositoryOptions.Name)))).OfType<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (Text(Value(entry, nameof(RepositoryOptions.Name))) is not null)
            {
                continue;
            }

            var suggestion = RepositoryName.Suggest(Text(Value(entry, nameof(RepositoryOptions.Path))));
            var name = suggestion;
            for (var number = 2; !taken.Add(name); number++)
            {
                var suffix = "-" + number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                name = suggestion[..Math.Min(suggestion.Length, RepositoryName.MAX_LENGTH - suffix.Length)] + suffix;
            }

            Set(entry, nameof(RepositoryOptions.Name), name);
        }

        if (active is not null && Text(section[activeKey]) is null)
        {
            section[activeKey] = Text(Value(active, nameof(RepositoryOptions.Name)));
        }
    }

    /// <summary>
    /// Reads the path of the repository the node works with from options that may not be valid yet.
    /// </summary>
    /// <param name="options">The options; may be <see langword="null"/>.</param>
    /// <returns>The path, or <see langword="null"/> when the node has no repository.</returns>
    public static string? ActivePath(SyncBridgeOptions? options)
    {
        if (options is null || options.Repositories.Count == 0)
        {
            return null;
        }

        var name = options.ActiveRepository?.Trim();
        var active = string.IsNullOrEmpty(name)
            ? null
            : options.Repositories.FirstOrDefault(repository => string.Equals(repository.Name?.Trim(), name, StringComparison.OrdinalIgnoreCase));
        var path = (active ?? options.Repositories[0]).Path;
        return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
    }

    /// <summary>
    /// Reads a property without regard to the case of its name.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static JsonNode? Value(JsonObject node, string property)
        => FindKey(node, property) is { } key ? node[key] : null;

    /// <summary>
    /// Sets a property, keeping the spelling of its name in the file.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value.</param>
    private static void Set(JsonObject node, string property, string value)
        => node[FindKey(node, property) ?? property] = value;

    /// <summary>
    /// Finds the name of a property as the file spells it.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The name as spelled in the file, or <see langword="null"/>.</returns>
    private static string? FindKey(JsonObject node, string property)
        => node.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(property, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Reads a non-blank string value.
    /// </summary>
    /// <param name="node">The node; may be <see langword="null"/>.</param>
    /// <returns>The trimmed text, or <see langword="null"/>.</returns>
    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;
}
