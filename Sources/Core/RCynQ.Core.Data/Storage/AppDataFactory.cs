using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;

namespace RCynQ.Core.Data.Storage;

/// <summary>
/// Resolves and creates the local application data directories.
/// </summary>
/// <remarks>
/// The root is resolved with <see cref="ConfigLocations.ResolveDataRoot"/>: <c>DataRoot</c>, the <c>data</c> directory of a
/// portable installation, or <c>%LOCALAPPDATA%/RCynQ/&lt;nodeId&gt;</c>. The keys directory is restricted to the owner.
/// </remarks>
public sealed class AppDataFactory : IAppDataFactory
{
    /// <summary>
    /// The lazily created path set.
    /// </summary>
    private readonly Lazy<AppDataPaths> _paths;

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDataFactory"/> class from node options.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public AppDataFactory(SyncBridgeOptions options)
        : this(AppDataPaths.FromRoot(ResolveRoot(options)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AppDataFactory"/> class with explicit paths.
    /// </summary>
    /// <param name="paths">The path set.</param>
    /// <exception cref="ArgumentNullException"><paramref name="paths"/> is <see langword="null"/>.</exception>
    public AppDataFactory(AppDataPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        _paths = new Lazy<AppDataPaths>(() => EnsureCreated(paths), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public AppDataPaths Create() => _paths.Value;

    /// <summary>
    /// Resolves the root directory for the given options.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <returns>The root directory.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static string ResolveRoot(SyncBridgeOptions options)
        => ConfigLocations.ResolveDataRoot(options, null, ConfigLocations.FindPortableRoot());

    /// <summary>
    /// Creates all directories of the path set.
    /// </summary>
    /// <param name="paths">The path set.</param>
    /// <returns>The same path set.</returns>
    private static AppDataPaths EnsureCreated(AppDataPaths paths)
    {
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.DatabasePath) ?? paths.Root);
        Directory.CreateDirectory(paths.PatchesDirectory);
        Directory.CreateDirectory(paths.BackupsDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
        Directory.CreateDirectory(paths.SnapshotsDirectory);
        Directory.CreateDirectory(paths.TempDirectory);
        Directory.CreateDirectory(paths.KeysDirectory);
        FilePermissions.RestrictDirectoryToOwner(paths.KeysDirectory);

        // Records the computer that created the node, so a copied installation is recognized on another computer.
        InstallationStamp.Check(paths.Root, Environment.MachineName);
        return paths;
    }
}
