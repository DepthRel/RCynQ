using RCynQ.Core.Abstractions.Storage;

namespace RCynQ.Core.Config;

/// <summary>
/// Default <see cref="INodeDataEraser"/>: removes the files and folders <see cref="AppDataPaths.NodeEntries"/> names in
/// the data directory, the directory itself when nothing else is left in it, and then the configuration file.
/// </summary>
/// <remarks>
/// <para>
/// The data directory may be any folder the configuration names, so nothing is removed from it by a wildcard: only the
/// entries RCynQ keeps there, and only when the folder holds the data of a node at all. An entry that is, or holds, a
/// repository of the node is never removed. The settings of the user interface next to the configuration file stay.
/// </para>
/// </remarks>
public sealed class NodeDataEraser : INodeDataEraser
{
    /// <summary>
    /// How paths are compared: without regard to case where the file system ignores it.
    /// </summary>
    private static readonly StringComparison _pathComparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <inheritdoc />
    public NodeResetPlan Plan(SyncBridgeOptions options, string configPath, string? portableRoot)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);

        var fullConfigPath = Path.GetFullPath(configPath);
        var paths = AppDataPaths.FromRoot(ConfigLocations.ResolveDataRoot(options, fullConfigPath, portableRoot));
        var hasData = File.Exists(paths.DatabasePath) ||
            Directory.Exists(paths.KeysDirectory) ||
            File.Exists(Path.Combine(paths.Root, InstallationStamp.FILE_NAME));

        var configDirectory = Path.GetDirectoryName(fullConfigPath)!;
        var repositories = options.Repositories
            .Select(repository => repository.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path.Trim(), configDirectory))
            .ToArray();

        var entries = hasData
            ? paths.NodeEntries
                .Where(entry => File.Exists(entry) || Directory.Exists(entry))
                .Where(entry => !repositories.Any(repository => IsSameOrInside(repository, entry)))
                .ToArray()
            : Array.Empty<string>();

        return new NodeResetPlan(options.NodeId, fullConfigPath, paths.Root, hasData, entries);
    }

    /// <inheritdoc />
    public NodeResetReport Erase(NodeResetPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var removed = 0;
        var failed = new List<string>();
        foreach (var entry in plan.Entries)
        {
            try
            {
                Delete(entry);
                removed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(entry);
            }
        }

        // The configuration stays while data of the node is left: the reset can then be run again as it is.
        if (failed.Count > 0)
        {
            return new NodeResetReport(removed, failed);
        }

        RemoveIfEmpty(plan.DataRoot);
        try
        {
            if (File.Exists(plan.ConfigPath))
            {
                File.SetAttributes(plan.ConfigPath, FileAttributes.Normal);
                File.Delete(plan.ConfigPath);
                removed++;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failed.Add(plan.ConfigPath);
        }

        return new NodeResetReport(removed, failed);
    }

    /// <summary>
    /// Tells whether a path is a folder or lies inside it.
    /// </summary>
    /// <param name="path">The full path.</param>
    /// <param name="folder">The full path of the folder.</param>
    /// <returns><see langword="true"/> when removing <paramref name="folder"/> would remove <paramref name="path"/>.</returns>
    private static bool IsSameOrInside(string path, string folder)
    {
        var trimmedPath = Path.TrimEndingDirectorySeparator(path);
        var trimmedFolder = Path.TrimEndingDirectorySeparator(folder);
        return trimmedPath.Equals(trimmedFolder, _pathComparison) ||
            trimmedPath.StartsWith(trimmedFolder + Path.DirectorySeparatorChar, _pathComparison) ||
            trimmedPath.StartsWith(trimmedFolder + Path.AltDirectorySeparatorChar, _pathComparison);
    }

    /// <summary>
    /// Removes a file, or a folder with everything in it; read-only files (Git keeps its objects so) are made writable
    /// first.
    /// </summary>
    /// <param name="path">The path.</param>
    private static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return;
        }

        if (!Directory.Exists(path))
        {
            return;
        }

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (UnauthorizedAccessException)
        {
            ClearReadOnly(new DirectoryInfo(path));
            Directory.Delete(path, recursive: true);
        }
    }

    /// <summary>
    /// Makes every file and folder below a folder writable.
    /// </summary>
    /// <param name="folder">The folder.</param>
    private static void ClearReadOnly(DirectoryInfo folder)
    {
        foreach (var item in folder.EnumerateFileSystemInfos("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 }))
        {
            if (item.Attributes.HasFlag(FileAttributes.ReadOnly))
            {
                item.Attributes &= ~FileAttributes.ReadOnly;
            }
        }

        folder.Attributes &= ~FileAttributes.ReadOnly;
    }

    /// <summary>
    /// Removes the data directory when nothing is left in it.
    /// </summary>
    /// <param name="folder">The data directory.</param>
    private static void RemoveIfEmpty(string folder)
    {
        try
        {
            if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An empty folder left behind holds nothing of the node; the next start fills it again.
        }
    }
}
