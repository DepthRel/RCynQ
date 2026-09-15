using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Config;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// The services of one node, built from its configuration file for the duration of a command.
/// </summary>
public sealed class NodeContext : IAsyncDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NodeContext"/> class.
    /// </summary>
    /// <param name="services">The node service provider; disposed with the context.</param>
    /// <param name="options">The node options.</param>
    /// <param name="configPath">The configuration file path.</param>
    public NodeContext(IServiceProvider services, SyncBridgeOptions options, string configPath)
    {
        Services = services;
        Options = options;
        ConfigPath = configPath;
    }

    /// <summary>Gets the node service provider.</summary>
    public IServiceProvider Services { get; }

    /// <summary>Gets the node options.</summary>
    public SyncBridgeOptions Options { get; }

    /// <summary>Gets the configuration file path.</summary>
    public string ConfigPath { get; }

    /// <summary>
    /// Resolves a required service.
    /// </summary>
    /// <typeparam name="T">The service type.</typeparam>
    /// <returns>The service.</returns>
    public T Get<T>()
        where T : notnull
        => Services.GetRequiredService<T>();

    /// <summary>
    /// Disposes the service provider.
    /// </summary>
    /// <returns>A task representing the operation.</returns>
    public async ValueTask DisposeAsync()
    {
        switch (Services)
        {
            case IAsyncDisposable asyncDisposable:
                await asyncDisposable.DisposeAsync();
                break;
            case IDisposable disposable:
                disposable.Dispose();
                break;
        }
    }
}
