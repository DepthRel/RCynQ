using Avalonia;
using Avalonia.Headless;

namespace RCynQ.UI.Headless.Tests;

/// <summary>
/// Builds the application for the headless platform.
/// </summary>
public static class TestApp
{
    /// <summary>
    /// Configures Avalonia without a screen.
    /// </summary>
    /// <returns>The application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
