namespace RCynQ.Ssh.Server;

/// <summary>
/// Describes the computer the program runs on.
/// </summary>
public interface IHostSystemSource
{
    /// <summary>
    /// Describes the computer: its system, distribution and tools.
    /// </summary>
    /// <returns>The description.</returns>
    HostSystem Describe();
}
