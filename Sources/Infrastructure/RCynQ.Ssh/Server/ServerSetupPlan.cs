namespace RCynQ.Ssh.Server;

/// <summary>
/// What it takes to change the computer: the command to run with administrator rights, and the same commands for the
/// user to run by hand when that is not possible or fails.
/// </summary>
/// <param name="Command">The command, or <see langword="null"/> when the computer cannot be changed automatically.</param>
/// <param name="ManualCommands">The commands to run by hand in a terminal of an administrator, one per line.</param>
/// <param name="ReasonKey">
/// The localization key of the reason why there is no <paramref name="Command"/>, or <see langword="null"/>.
/// </param>
public sealed record ServerSetupPlan(ElevatedCommand? Command, string ManualCommands, string? ReasonKey = null)
{
    /// <summary>
    /// Creates a plan for a computer that cannot be changed automatically.
    /// </summary>
    /// <param name="reasonKey">The localization key of the reason.</param>
    /// <param name="manualCommands">The commands to run by hand.</param>
    /// <returns>The plan.</returns>
    public static ServerSetupPlan Manual(string reasonKey, string manualCommands) => new(null, manualCommands, reasonKey);
}
