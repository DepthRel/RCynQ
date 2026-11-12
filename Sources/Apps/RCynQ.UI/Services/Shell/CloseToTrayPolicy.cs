using Avalonia.Controls;

namespace RCynQ.UI.Services.Shell;

/// <summary>
/// Decides whether closing the main window only hides it in the notification area.
/// </summary>
public static class CloseToTrayPolicy
{
    /// <summary>
    /// Tells whether a closing of the main window becomes hiding.
    /// </summary>
    /// <param name="reason">Why the window closes.</param>
    /// <param name="isProgrammatic">Whether the application closes the window itself.</param>
    /// <param name="closeToTray">Whether the user keeps the application running when the window is closed.</param>
    /// <returns>
    /// <see langword="true"/> only for the user closing the window (its close button, Alt+F4) while closing to the tray
    /// is on; the end of the application, of the session or of the operating system always closes it.
    /// </returns>
    public static bool ShouldHide(WindowCloseReason reason, bool isProgrammatic, bool closeToTray)
        => closeToTray && !isProgrammatic && reason == WindowCloseReason.WindowClosing;
}
