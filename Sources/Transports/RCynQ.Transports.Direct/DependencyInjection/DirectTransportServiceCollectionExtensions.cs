using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.Direct.Links;

namespace RCynQ.Transports.Direct.DependencyInjection;

/// <summary>
/// Registers the Direct transport.
/// </summary>
public static class DirectTransportServiceCollectionExtensions
{
    /// <summary>
    /// Registers the Direct transport provider. Requires an <see cref="ITcpProbe"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddDirectTransport(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITransportLinkProvider, DirectLinkProvider>());
        return services;
    }
}
