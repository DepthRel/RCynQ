using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Transports.Abstractions.Factory;
using RCynQ.Transports.Abstractions.Management;

namespace RCynQ.Transports.Abstractions.DependencyInjection;

/// <summary>
/// Registers the transport abstractions.
/// </summary>
public static class TransportsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the transport link factory and the link manager. Transport packages add their providers separately.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRCynQTransports(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher>(NullEventPublisher.Instance);
        services.TryAddSingleton<TransportLinkFactory>();
        services.TryAddSingleton<ITransportLinkFactory>(provider => provider.GetRequiredService<TransportLinkFactory>());
        services.TryAddSingleton<ILinkManager, LinkManager>();
        return services;
    }
}
