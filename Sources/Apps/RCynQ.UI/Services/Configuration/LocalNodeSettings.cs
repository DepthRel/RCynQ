using RCynQ.Core.Config;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// <see cref="ILocalNodeSettings"/> backed by the local <c>syncbridge.json</c>.
/// </summary>
public sealed class LocalNodeSettings : ILocalNodeSettings
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LocalNodeSettings"/> class.
    /// </summary>
    /// <param name="configPath">The path of the configuration file.</param>
    public LocalNodeSettings(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ConfigPath = configPath;
    }

    /// <inheritdoc />
    public string ConfigPath { get; }

    /// <inheritdoc />
    public SyncBridgeOptions? Read() => LocalConfigFile.TryLoad(ConfigPath);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> SaveAsync(NodeSetupValues values, CancellationToken cancellationToken = default)
        => LocalConfigFile.SaveAsync(ConfigPath, values, cancellationToken);
}
