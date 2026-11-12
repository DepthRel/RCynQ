namespace RCynQ.UI.Services.Shell;

/// <summary>
/// Whether closing the main window keeps the application running with an icon in the notification area.
/// </summary>
public interface ITrayPreferences
{
    /// <summary>
    /// Gets a value indicating whether closing the main window only hides it, so the node keeps working.
    /// </summary>
    bool CloseToTray { get; }

    /// <summary>
    /// Stores whether closing the main window only hides it.
    /// </summary>
    /// <param name="enabled">The new value.</param>
    /// <exception cref="IOException">The settings could not be written.</exception>
    /// <exception cref="UnauthorizedAccessException">The settings could not be written.</exception>
    void SetCloseToTray(bool enabled);
}
