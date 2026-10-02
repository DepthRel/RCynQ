using RCynQ.Core.Config;
using RCynQ.Localization;

namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// <see cref="ILocalNodeSettings"/> backed by the local <c>syncbridge.json</c>.
/// </summary>
public sealed class LocalNodeSettings : ILocalNodeSettings
{
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalNodeSettings"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="configPath">The path of the configuration file.</param>
    public LocalNodeSettings(ILocalizer localizer, string configPath)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        ConfigPath = configPath;
    }

    /// <inheritdoc />
    public string ConfigPath { get; }

    /// <inheritdoc />
    public SyncBridgeOptions? Read() => LocalConfigFile.TryLoad(ConfigPath);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> SaveAsync(NodeSetupValues values, CancellationToken cancellationToken = default)
        => LocalConfigFile.SaveAsync(ConfigPath, values, _localizer, cancellationToken);
}
