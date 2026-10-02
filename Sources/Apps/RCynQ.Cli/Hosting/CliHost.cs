using CliFx;
using CliFx.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Localization;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// Builds the CliFx application with all commands.
/// </summary>
public static class CliHost
{
    /// <summary>
    /// The executable name shown in help.
    /// </summary>
    public const string EXECUTABLE_NAME = "rcynq";

    /// <summary>
    /// Creates the application.
    /// </summary>
    /// <param name="contexts">The node context factory handed to commands.</param>
    /// <param name="localizer">The localizer of the messages the commands print.</param>
    /// <param name="console">The console, or <see langword="null"/> for the system console.</param>
    /// <returns>The application.</returns>
    public static CliApplication CreateApplication(INodeContextFactory contexts, ILocalizer localizer, IConsole? console = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(localizer);
        var services = new ServiceCollection();
        services.AddSingleton(contexts);
        services.AddSingleton(localizer);
        foreach (var command in typeof(CliHost).Assembly.GetTypes().Where(type => typeof(ICommand).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false }))
        {
            services.AddTransient(command);
        }

        var provider = services.BuildServiceProvider();
        var builder = new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .SetExecutableName(EXECUTABLE_NAME)
            .SetTitle("RCynQ")
            .SetDescription("Headless management of an RCynQ (SyncBridge) node: role, links, transports, live patches, Git channel and diagnostics.")
            .UseTypeActivator(type => provider.GetRequiredService(type));

        if (console is not null)
        {
            builder.UseConsole(console);
        }

        return builder.Build();
    }
}
