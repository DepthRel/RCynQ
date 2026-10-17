namespace RCynQ.Core.Enums;

/// <summary>
/// How a change of the computer that needs administrator rights ended: installing the SSH server or opening a port.
/// </summary>
public enum ServerSetupOutcome
{
    /// <summary>
    /// The commands ran and ended successfully.
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The user dismissed the administrator prompt; nothing was changed.
    /// </summary>
    Cancelled = 1,

    /// <summary>
    /// The commands ran and failed; the user gets the commands to run by hand.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// The computer cannot be changed automatically (no package manager, no way to ask for the password); the user gets
    /// the commands to run by hand.
    /// </summary>
    Unsupported = 3,

    /// <summary>
    /// Nothing had to be changed: the system lets the connections through by itself.
    /// </summary>
    NotNeeded = 4
}
