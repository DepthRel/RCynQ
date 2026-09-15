using RCynQ.Cli.Hosting;

namespace RCynQ.Cli;

/// <summary>
/// Entry point of the <c>rcynq</c> command-line interface.
/// </summary>
public static class Program
{
    /// <summary>
    /// Runs the command given on the command line.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>The exit code: <c>0</c> success, <c>1</c> command failure, <c>2</c> invalid configuration or usage.</returns>
    public static async Task<int> Main(string[] args)
        => await CliHost.CreateApplication(new NodeContextFactory()).RunAsync(args);
}
