namespace RCynQ.Ssh.Enums;

/// <summary>
/// The operating system family whose commands set up the SSH server or open a port.
/// </summary>
public enum HostPlatform
{
    /// <summary>
    /// Windows: PowerShell behind the UAC prompt.
    /// </summary>
    Windows = 0,

    /// <summary>
    /// Linux: a shell behind <c>pkexec</c>.
    /// </summary>
    Linux = 1,

    /// <summary>
    /// macOS: a shell behind the administrator prompt of <c>osascript</c>.
    /// </summary>
    MacOs = 2,

    /// <summary>
    /// Any other system; nothing is set up automatically.
    /// </summary>
    Other = 3
}
