namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// Starts child processes.
/// </summary>
public interface IChildProcessLauncher
{
    /// <summary>
    /// Starts a process without a console window, passing the arguments without shell interpretation.
    /// </summary>
    /// <param name="fileName">The executable.</param>
    /// <param name="arguments">The discrete arguments.</param>
    /// <param name="workingDirectory">The working directory.</param>
    /// <returns>The started process.</returns>
    IChildProcess Start(string fileName, IReadOnlyList<string> arguments, string workingDirectory);
}
