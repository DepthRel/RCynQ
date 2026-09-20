using RCynQ.Core.Config;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// Reads and writes the node settings that live in the configuration file rather than in the service API,
/// such as the shared link token and the endpoint mode.
/// </summary>
public interface ILocalNodeSettings
{
    /// <summary>
    /// Gets the path of the configuration file.
    /// </summary>
    string ConfigPath { get; }

    /// <summary>
    /// Reads the current settings.
    /// </summary>
    /// <returns>The settings, or <see langword="null"/> when the file is missing or damaged.</returns>
    SyncBridgeOptions? Read();

    /// <summary>
    /// Writes the settings, keeping every value the file holds beyond them.
    /// </summary>
    /// <param name="values">The values to write.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The validation errors; empty when the file was written.</returns>
    Task<IReadOnlyList<string>> SaveAsync(NodeSetupValues values, CancellationToken cancellationToken = default);
}
