using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RCynQ.UI.Services.Api;
using RCynQ.UI.Services.Configuration;
using RCynQ.UI.Services.Connection;
using RCynQ.UI.Services.Dialogs;
using RCynQ.UI.Services.Dispatching;
using RCynQ.UI.Services.Events;
using RCynQ.UI.Services.Hosting;
using RCynQ.UI.ViewModels;
using RCynQ.UI.ViewModels.Navigation;
using RCynQ.UI.ViewModels.Pages;
using RCynQ.UI.ViewModels.Setup;
using RCynQ.UI.Views;

namespace RCynQ.UI;

/// <summary>
/// The Avalonia application: loads the theme, composes services and view models, starts the local service with the
/// main window and stops it when the application exits.
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
            var args = desktop.Args ?? Array.Empty<string>();
            var settings = ServiceConnectionResolver.Resolve(args);
            var tokens = new ApiTokenProvider(settings);
            var api = new ServiceApiClient(settings, tokens);
            var events = new SignalRNodeEventStream(settings, tokens);
            var command = ServiceExecutableLocator.Locate(AppContext.BaseDirectory, args, Environment.GetEnvironmentVariable, OperatingSystem.IsWindows());
            var controller = new ServiceProcessController(settings, api, new SystemChildProcessLauncher(), command);

            var cli = ExecutableLocator.Locate(AppContext.BaseDirectory, "rcynq", "cli", "RCynQ.Cli", OperatingSystem.IsWindows(), Environment.GetEnvironmentVariable);

            Window? owner = null;
            var dialogs = new AvaloniaDialogService(() => owner);
            var main = new MainViewModel(
                api,
                events,
                new AvaloniaUiDispatcher(),
                new RoleAdapter(),
                controller,
                new SetupViewModel(settings.ConfigPath, dialogs),
                new OverviewViewModel(api),
                new RoleAndNodesViewModel(api, dialogs, new ElevatedCliRunner(settings.ConfigPath, cli), new LocalNodeSettings(settings.ConfigPath)),
                new SyncViewModel(api, dialogs),
                new TunnelViewModel(api),
                new DiagnosticsViewModel(api, dialogs),
                new BranchViewModel(api),
                new LogsViewModel(api, dialogs));

            var window = new MainWindow { DataContext = main };
            owner = window;
            window.Opened += async (_, _) => await main.InitializeAsync();
            desktop.MainWindow = window;
            desktop.Exit += (_, _) =>
            {
                Task.Run(() => events.DisposeAsync().AsTask()).Wait(TimeSpan.FromSeconds(3));

                // The service started by the UI is stopped with it; it closes its tunnels and SSH forwards on shutdown.
                Task.Run(() => controller.StopAsync()).Wait(ExitStopTimeout);
                controller.Dispose();
                api.Dispose();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
