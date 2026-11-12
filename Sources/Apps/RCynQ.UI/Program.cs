using Avalonia;
using RCynQ.UI.Services.Connection;
using RCynQ.UI.Services.Shell;

namespace RCynQ.UI;

/// <summary>
/// Entry point of the RCynQ desktop client.
/// </summary>
public static class Program
{
    /// <summary>
    /// How long a second copy of the application waits for the first one to take its request to show the window.
    /// </summary>
    private static readonly TimeSpan _signalTimeout = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Starts the Avalonia application, or shows the window of the copy already running for the same node.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments: <c>--config &lt;syncbridge.json&gt;</c>, <c>--service &lt;url&gt;</c>,
    /// <c>--token-file &lt;path&gt;</c> or <c>--token &lt;value&gt;</c>.
    /// </param>
    [STAThread]
    public static void Main(string[] args)
    {
        // One copy per node: a second start (a click on the shortcut while the window sits in the notification area)
        // shows the window of the first copy instead of starting a second one next to the same service.
        var key = SingleInstanceGuard.KeyFor(ServiceConnectionResolver.Resolve(args).ConfigPath);
        using var guard = SingleInstanceGuard.TryAcquire(key);
        if (guard is null)
        {
            SingleInstanceGuard.SignalExisting(key, _signalTimeout);
            return;
        }

        Configure(() => new App(guard)).StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Configures Avalonia; also used by the XAML previewer.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp() => Configure(() => new App());

    /// <summary>
    /// Configures Avalonia with the application made by a factory.
    /// </summary>
    /// <param name="factory">Makes the application.</param>
    /// <returns>The application builder.</returns>
    private static AppBuilder Configure(Func<App> factory)
        => AppBuilder.Configure(factory)
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
