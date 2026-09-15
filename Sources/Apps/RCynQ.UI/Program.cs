using Avalonia;

namespace RCynQ.UI;

/// <summary>
/// Entry point of the RCynQ desktop client.
/// </summary>
public static class Program
{
    /// <summary>
    /// Starts the Avalonia application.
    /// </summary>
    /// <param name="args">
    /// Command-line arguments: <c>--config &lt;syncbridge.json&gt;</c>, <c>--service &lt;url&gt;</c>,
    /// <c>--token-file &lt;path&gt;</c> or <c>--token &lt;value&gt;</c>.
    /// </param>
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    /// <summary>
    /// Configures Avalonia; also used by the XAML previewer.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
