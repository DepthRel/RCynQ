namespace RCynQ.UI.Services.Updates;

/// <summary>
/// The choices of the user about new versions.
/// </summary>
public interface IUpdatePreferences
{
    /// <summary>
    /// Gets a value indicating whether the application looks for a new version by itself.
    /// </summary>
    bool CheckEnabled { get; }

    /// <summary>
    /// Gets the version the user asked not to be reminded of, or <see langword="null"/>.
    /// </summary>
    Version? SkippedVersion { get; }

    /// <summary>
    /// Stores whether the application looks for a new version by itself.
    /// </summary>
    /// <param name="enabled">The new value.</param>
    /// <exception cref="IOException">The settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The settings could not be written.</exception>
    void SetCheckEnabled(bool enabled);

    /// <summary>
    /// Stores the version the user asked not to be reminded of.
    /// </summary>
    /// <param name="version">The version.</param>
    /// <exception cref="IOException">The settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The settings could not be written.</exception>
    void Skip(Version version);
}
