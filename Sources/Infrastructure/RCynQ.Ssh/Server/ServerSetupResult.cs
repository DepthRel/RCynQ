using RCynQ.Core.Enums;

namespace RCynQ.Ssh.Server;

/// <summary>
/// How installing the SSH server or opening a port ended, worded for the user.
/// </summary>
/// <param name="Outcome">The outcome.</param>
/// <param name="Message">The localized message.</param>
/// <param name="ManualCommands">
/// The commands to run by hand when the change did not happen, or <see langword="null"/> when there is nothing to do.
/// </param>
public sealed record ServerSetupResult(ServerSetupOutcome Outcome, string Message, string? ManualCommands = null)
{
    /// <summary>
    /// Gets a value indicating whether the computer is ready: the change was made or was not needed.
    /// </summary>
    public bool IsReady => Outcome is ServerSetupOutcome.Completed or ServerSetupOutcome.NotNeeded;
}
