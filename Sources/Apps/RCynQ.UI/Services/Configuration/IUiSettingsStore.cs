namespace RCynQ.UI.Services.Configuration;

/// <summary>
/// Keeps the settings of the user interface itself (the interface language), as opposed to the settings of the node,
/// which live in <c>syncbridge.json</c>.
/// </summary>
public interface IUiSettingsStore
{
    /// <summary>
    /// Reads a setting.
    /// </summary>
    /// <param name="key">The name of the setting.</param>
    /// <returns>The value, or <see langword="null"/> when it was never stored or the store is unreadable.</returns>
    string? Get(string key);

    /// <summary>
    /// Stores a setting, keeping every other value of the store.
    /// </summary>
    /// <param name="key">The name of the setting.</param>
    /// <param name="value">The value.</param>
    /// <exception cref="IOException">The store could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The store could not be written.</exception>
    void Set(string key, string value);
}
