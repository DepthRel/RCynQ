using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using RCynQ.Localization;
using RCynQ.UI.Localization;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Connection;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Dispatching;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.Services.Localization;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Dialogs;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.ViewModels.Setup;
using RCynQ.UI.Views;
using RCynQ.UI.Views.Dialogs;

namespace RCynQ.UI;

/// <summary>
/// The Avalonia application: loads the theme, chooses the language, shows the splash screen while it composes services
/// and view models and starts the local service, then opens the main window and stops the service when the
/// application exits.
/// </summary>
public sealed class App : Application
{
    /// <summary>
    /// The maximum time the application waits for the service to stop on exit.
    /// </summary>
    private static readonly TimeSpan ExitStopTimeout = TimeSpan.FromSeconds(20);

    /// <inheritdoc />
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Nothing is the main window yet: the splash must be able to close without ending the application.
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var args = desktop.Args ?? Array.Empty<string>();
            var settings = ServiceConnectionResolver.Resolve(args);

            // The language is stored next to the configuration file. Reading it takes a moment, so the splash already
            // speaks the language of the user; on the first start the language of the operating system is chosen here.
            var uiSettings = new JsonUiSettingsStore(JsonUiSettingsStore.PathFor(settings.ConfigPath));
            var language = new LanguageService(Texts.Localizer, uiSettings);
            language.Initialize();

            var splash = new SplashWindow();
            splash.Show();
            _ = StartAsync(desktop, splash, args, settings, uiSettings, language);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Prepares the application behind the splash and opens the main window when everything is ready, so the user never
    /// sees a window that is still loading.
    /// </summary>
    /// <param name="desktop">The application lifetime.</param>
    /// <param name="splash">The splash window.</param>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="settings">How the UI reaches the service.</param>
    /// <param name="uiSettings">The local settings of the UI.</param>
    /// <param name="language">The language service, already initialized.</param>
    /// <returns>A task that completes when the main window is shown.</returns>
    private static async Task StartAsync(
        IClassicDesktopStyleApplicationLifetime desktop,
        SplashWindow splash,
        IReadOnlyList<string> args,
        ServiceConnectionSettings settings,
        JsonUiSettingsStore uiSettings,
        ILanguageService language)
    {
        try
        {
            // Lets the splash draw its first frame before the work below keeps the UI thread busy.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            splash.ShowStep(Texts.Get("Splash.Preparing"));
            var tokens = new ApiTokenProvider(settings);
            var api = new ServiceApiClient(settings, tokens);
            var events = new SignalRNodeEventStream(settings, tokens);
            var controller = Compose(args, settings, api, out var cli);
            var (main, owner) = CreateViewModel(settings, uiSettings, language, api, events, controller, cli);

            // The service is started and the configuration is read before the window appears.
            splash.ShowStep(Texts.Get("Splash.StartingService"));
            try
            {
                await main.InitializeAsync();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                main.StartupError = Texts.Format("Splash.StartFailed", ex.Message);
            }

            splash.ShowStep(Texts.Get("Splash.OpeningWindow"));
            var window = new MainWindow { DataContext = main };
            owner.Value = window;

            // The window is drawn by now: the splash goes away without a gap.
            window.Opened += (_, _) => splash.Close();
            desktop.MainWindow = window;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            desktop.Exit += (_, _) =>
            {
                Task.Run(() => events.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));

                // The service started by the UI is stopped with it; it closes its tunnels and SSH forwards on shutdown.
                Task.Run(() => controller.StopAsync()).Wait(ExitStopTimeout);
                controller.Dispose();
                api.Dispose();
            };
            window.Show();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // The application cannot start: say so instead of leaving a process without a window.
            var failure = new TextDialog { DataContext = new TextDialogViewModel("RCynQ", Texts.Format("Splash.StartFailed", ex.Message)) };
            desktop.MainWindow = failure;
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
            failure.Show();
            splash.Close();
        }
    }

    /// <summary>
    /// Creates the controller that runs the local service, and locates the command line tool.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <param name="settings">How the UI reaches the service.</param>
    /// <param name="api">The API client.</param>
    /// <param name="cli">The command that starts the command line tool, or <see langword="null"/> when it was not found.</param>
    /// <returns>The controller.</returns>
    private static ServiceProcessController Compose(
        IReadOnlyList<string> args,
        ServiceConnectionSettings settings,
        IServiceApiClient api,
        out ServiceLaunchCommand? cli)
    {
        var command = ServiceExecutableLocator.Locate(AppContext.BaseDirectory, args, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
        cli = ExecutableLocator.Locate(AppContext.BaseDirectory, "rcynq", "cli", "RCynQ.Cli", OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable);
        return new ServiceProcessController(settings, api, new SystemChildProcessLauncher(), command);
    }

    /// <summary>
    /// Creates the main view model with the pages, dialogs and the language selector.
    /// </summary>
    /// <param name="settings">How the UI reaches the service.</param>
    /// <param name="uiSettings">The local settings of the UI.</param>
    /// <param name="language">The language service, already initialized.</param>
    /// <param name="api">The API client.</param>
    /// <param name="events">The live event stream.</param>
    /// <param name="controller">The controller of the local service.</param>
    /// <param name="cli">The command that starts the command line tool, or <see langword="null"/>.</param>
    /// <returns>The main view model and the holder of the window that owns the dialogs, filled when the window exists.</returns>
    private static (MainViewModel Main, OwnerHolder Owner) CreateViewModel(
        ServiceConnectionSettings settings,
        JsonUiSettingsStore uiSettings,
        ILanguageService language,
        IServiceApiClient api,
        INodeEventStream events,
        IServiceController controller,
        ServiceLaunchCommand? cli)
    {
        var owner = new OwnerHolder();
        var dialogs = new AvaloniaDialogService(() => owner.Value);
        var languageSelector = new LanguageViewModel(language, FlagImages.Load);
        var main = new MainViewModel(
            api,
            events,
            new AvaloniaUiDispatcher(),
            new RoleAdapter(),
            controller,
            new SetupViewModel(settings.ConfigPath, dialogs, languageSelector),
            new OverviewViewModel(api),
            new RoleAndNodesViewModel(api, dialogs, new ElevatedCliRunner(settings.ConfigPath, cli), new LocalNodeSettings(settings.ConfigPath)),
            new SyncViewModel(api, dialogs),
            new TunnelViewModel(api),
            new DiagnosticsViewModel(api, dialogs),
            new BranchViewModel(api),
            new LogsViewModel(api, dialogs),
            new SettingsViewModel(languageSelector, uiSettings.FilePath));
        return (main, owner);
    }

    /// <summary>
    /// Holds the window that owns the dialogs; the dialogs are created before the window exists.
    /// </summary>
    private sealed class OwnerHolder
    {
        /// <summary>
        /// Gets or sets the window, or <see langword="null"/> before it exists.
        /// </summary>
        public Window? Value { get; set; }
    }
}
