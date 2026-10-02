using System.Text.Json;
using System.Text.Json.Nodes;
using RCynQ.Core.Enums;

namespace RCynQ.Core.Config.Legacy;

/// <summary>
/// Brings a configuration file written by an earlier version to the current format.
/// </summary>
/// <remarks>
/// <para>
/// Every host (service, command line, user interface) calls <see cref="Migrate(string)"/> before it reads the file, so a
/// node that is updated keeps working without a new setup.
/// </para>
/// <para>
/// The migration is idempotent. A file that needs no change is not touched. Before the first change the file is copied
/// to a file with the suffix <see cref="BACKUP_SUFFIX"/>. A file that cannot be read or parsed is left alone: the regular
/// loading reports it.
/// </para>
/// </remarks>
public static class ConfigurationFileMigrator
{
    /// <summary>
    /// The suffix of the copy of the file made before it is changed.
    /// </summary>
    public const string BACKUP_SUFFIX = ".bak";

    /// <summary>
    /// The name of the property that holds the chain links.
    /// </summary>
    private const string CHAIN_PROPERTY = "Chain";

    /// <summary>
    /// The name of the property that holds the role of the peer of a chain link.
    /// </summary>
    private const string PEER_ROLE_PROPERTY = "PeerRole";

    /// <summary>
    /// The property an earlier version kept the path of the repository of what is now an Origin in.
    /// </summary>
    private const string LEGACY_REPO_PATH_PROPERTY = "RepoPath";

    /// <summary>
    /// The property an earlier version kept the path of the working copy in.
    /// </summary>
    private const string LEGACY_WORKDIR_PATH_PROPERTY = "WorkdirPath";

    /// <summary>
    /// The reading options: the file may carry comments and trailing commas.
    /// </summary>
    private static readonly JsonDocumentOptions _documentOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    /// <summary>
    /// The writing options.
    /// </summary>
    private static readonly JsonSerializerOptions _writeOptions = new() { WriteIndented = true };

    /// <summary>
    /// Migrates the configuration file in place.
    /// </summary>
    /// <param name="path">The path of the configuration file.</param>
    /// <returns><see langword="true"/> when the file was changed.</returns>
    public static bool Migrate(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (JsonNode.Parse(File.ReadAllText(path), documentOptions: _documentOptions) is not JsonObject root || !Migrate(root))
            {
                return false;
            }

            var backup = path + BACKUP_SUFFIX;
            if (!File.Exists(backup))
            {
                File.Copy(path, backup);
            }

            var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var temp = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temp, root.ToJsonString(_writeOptions));
                File.Move(temp, path, overwrite: true);
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Migrates a parsed configuration document.
    /// </summary>
    /// <param name="root">The root object of the document.</param>
    /// <returns><see langword="true"/> when the document was changed.</returns>
    public static bool Migrate(JsonObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (Find(root, SyncBridgeOptions.SECTION_NAME) is not JsonObject section)
        {
            return false;
        }

        var changed = MigrateRole(section, nameof(SyncBridgeOptions.Role));
        if (Find(section, CHAIN_PROPERTY) is JsonArray chain)
        {
            foreach (var link in chain.OfType<JsonObject>())
            {
                changed |= MigrateRole(link, PEER_ROLE_PROPERTY);
            }
        }

        return MigrateRepository(section) || changed;
    }

    /// <summary>
    /// Turns the single path of an earlier version into the first repository of the node.
    /// </summary>
    /// <remarks>
    /// Earlier versions knew one repository: <c>RepoPath</c> on what is now an Origin, <c>WorkdirPath</c> on what is now a
    /// Workstation. It becomes the default repository (<see cref="RepositoryEntry.DefaultId"/>), so the sync base and the
    /// history of the node stay where they are. An Origin names it after its folder; a Workstation leaves the name empty
    /// and takes the name the Origin uses at their first contact.
    /// </remarks>
    /// <param name="section">The configuration section.</param>
    /// <returns><see langword="true"/> when the section was changed.</returns>
    private static bool MigrateRepository(JsonObject section)
    {
        var repoKey = FindKey(section, LEGACY_REPO_PATH_PROPERTY);
        var workdirKey = FindKey(section, LEGACY_WORKDIR_PATH_PROPERTY);
        if (repoKey is null && workdirKey is null)
        {
            return false;
        }

        var repo = repoKey is null ? null : Text(section[repoKey]);
        var workdir = workdirKey is null ? null : Text(section[workdirKey]);
        var isOrigin = Text(Find(section, nameof(SyncBridgeOptions.Role))) is { } role &&
            string.Equals(role, nameof(NodeRole.Origin), StringComparison.OrdinalIgnoreCase);
        var path = isOrigin ? repo ?? workdir : workdir ?? repo;
        if (repoKey is not null)
        {
            section.Remove(repoKey);
        }

        if (workdirKey is not null)
        {
            section.Remove(workdirKey);
        }

        if (path is null || FindKey(section, nameof(SyncBridgeOptions.Repositories)) is not null)
        {
            return true;
        }

        var name = isOrigin ? RepositoryName.Suggest(path) : string.Empty;
        section[nameof(SyncBridgeOptions.Repositories)] = new JsonArray(new JsonObject
        {
            [nameof(RepositoryOptions.Id)] = RepositoryEntry.DefaultId.ToString(),
            [nameof(RepositoryOptions.Name)] = name,
            [nameof(RepositoryOptions.Path)] = path
        });
        if (name.Length > 0)
        {
            section[nameof(SyncBridgeOptions.ActiveRepository)] = name;
        }

        return true;
    }

    /// <summary>
    /// Reads a non-blank string value.
    /// </summary>
    /// <param name="node">The node; may be <see langword="null"/>.</param>
    /// <returns>The trimmed text, or <see langword="null"/> when the node is not a non-blank string.</returns>
    private static string? Text(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text.Trim() : null;

    /// <summary>
    /// Replaces the role name of an earlier version in one property.
    /// </summary>
    /// <param name="node">The object that holds the property.</param>
    /// <param name="property">The property name, matched without regard to case.</param>
    /// <returns><see langword="true"/> when the value was replaced.</returns>
    private static bool MigrateRole(JsonObject node, string property)
    {
        var key = FindKey(node, property);
        if (key is null || node[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
        {
            return false;
        }

        if (!LegacyRoleNames.TryTranslate(text, out var role))
        {
            return false;
        }

        node[key] = role.ToString();
        return true;
    }

    /// <summary>
    /// Finds a property without regard to the case of its name.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The value, or <see langword="null"/>.</returns>
    private static JsonNode? Find(JsonObject node, string property)
        => FindKey(node, property) is { } key ? node[key] : null;

    /// <summary>
    /// Finds the name of a property as the file spells it.
    /// </summary>
    /// <param name="node">The object.</param>
    /// <param name="property">The property name.</param>
    /// <returns>The name as spelled in the file, or <see langword="null"/>.</returns>
    private static string? FindKey(JsonObject node, string property)
        => node.Select(pair => pair.Key).FirstOrDefault(key => key.Equals(property, StringComparison.OrdinalIgnoreCase));
}
