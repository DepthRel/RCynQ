using RCynQ.Core.Exceptions;
using RCynQ.Service.Hosting;
using RCynQ.Localization;

namespace RCynQ.Service;

/// <summary>
/// Entry point of the RCynQ background service.
/// </summary>
public static class Program
{
    /// <summary>
    /// Builds and runs the service host.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments. <c>--config &lt;path&gt;</c> selects the configuration file; any
    /// <c>--SyncBridge:Key=value</c> argument overrides configuration values.
    /// </param>
    /// <returns><c>0</c> after a clean shutdown, <c>1</c> on a runtime failure, <c>2</c> for invalid configuration.</returns>
    public static async Task<int> Main(string[] args)
    {
        Microsoft.AspNetCore.Builder.WebApplication app;
        try
        {
            app = ServiceHostBuilder.Build(args);
        }
        catch (SyncBridgeException ex)
        {
            // The configuration could not be read, so the application was never built; the message follows the language
            // the service was started with.
            await Console.Error.WriteLineAsync(ServiceHostBuilder.CreateLocalizer(args).Describe(ex));
            return 2;
        }

        try
        {
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ServiceHostBuilder.CreateLocalizer(args).Format("Service.Crashed", ex));
            return 1;
        }
        finally
        {
            await app.DisposeAsync();
        }
    }
}
