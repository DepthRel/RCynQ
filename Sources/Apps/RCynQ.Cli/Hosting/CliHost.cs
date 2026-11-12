using CliFx;
using CliFx.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Cli.Services;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
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
    /// <param name="configure">
    /// Replaces services the commands use outside a node context (the tests put stand-ins for the file system and the
    /// local service there), or <see langword="null"/>.
    /// </param>
    /// <returns>The application.</returns>
    public static CliApplication CreateApplication(INodeContextFactory contexts, ILocalizer localizer, IConsole? console = null, Action<IServiceCollection>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(localizer);
        var services = new ServiceCollection();
        services.AddSingleton(contexts);
        services.AddSingleton(localizer);
        configure?.Invoke(services);
        services.TryAddSingleton<INodeDataEraser, NodeDataEraser>();
        services.TryAddSingleton<ILocalServiceProbe, HttpLocalServiceProbe>();
        foreach (var command in typeof(CliHost).Assembly.GetTypes().Where(type => typeof(ICommand).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false }))
        {
            services.AddTransient(command);
        }

        var provider = services.BuildServiceProvider();
        var builder = new CliApplicationBuilder()
            .AddCommandsFromThisAssembly()
            .SetExecutableName(EXECUTABLE_NAME)
            .SetTitle("RCynQ")
            .SetDescription(localizer.Get("Cli.Description"))
            .UseTypeActivator(type => provider.GetRequiredService(type));

        if (console is not null)
        {
            builder.UseConsole(console);
        }

        return builder.Build();
    }
}
