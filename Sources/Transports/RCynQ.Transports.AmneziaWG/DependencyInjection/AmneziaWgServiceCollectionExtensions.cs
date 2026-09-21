using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.AmneziaWG.Cli;
using RCynQ.Transports.AmneziaWG.Links;

namespace RCynQ.Transports.AmneziaWG.DependencyInjection;

/// <summary>
/// Registers the AmneziaWG transport.
/// </summary>
public static class AmneziaWgServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AmneziaWG CLI adapter and link provider. Requires an <see cref="IProcessRunner"/> and an <see cref="ITcpProbe"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddAmneziaWgTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new AmneziaWgOptions());
        services.TryAddSingleton<IAmneziaWgCli, ProcessAmneziaWgCli>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransportLinkProvider, AmneziaWgLinkProvider>());
        return services;
    }
}
