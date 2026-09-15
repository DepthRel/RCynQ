using RCynQ.Core.Config;

namespace RCynQ.Core.Abstractions.Storage;

/// <summary>
/// Resolves the application data paths for a SyncBridge node.
/// </summary>
public interface IAppDataFactory
{
    /// <summary>
    /// Creates the application data paths for this node.
    /// </summary>
    /// <returns>
    /// The resolved application data paths, including the database file and
    /// the patches, backups, keys, and logs directories.
    /// </returns>
    AppDataPaths Create();
}
