using RCynQ.Cli.Hosting;
using RCynQ.Localization;

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
    {
        // One localizer for the commands and for the services of the node they work on.
        var localizer = new Localizer(LanguageCatalog.LoadEmbedded());
        return await CliHost.CreateApplication(new NodeContextFactory(localizer), localizer).RunAsync(args);
    }
}
