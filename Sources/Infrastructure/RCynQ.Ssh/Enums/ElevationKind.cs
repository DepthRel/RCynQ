namespace RCynQ.Ssh.Enums;

/// <summary>
/// How a command asks for administrator rights; it decides how a dismissed prompt is recognized.
/// </summary>
public enum ElevationKind
{
    /// <summary>
    /// The Windows UAC prompt (<c>runas</c>).
    /// </summary>
    WindowsUac = 0,

    /// <summary>
    /// The polkit prompt of <c>pkexec</c> on Linux.
    /// </summary>
    Polkit = 1,

    /// <summary>
    /// The administrator prompt of <c>osascript</c> on macOS.
    /// </summary>
    MacAdministrator = 2
}
