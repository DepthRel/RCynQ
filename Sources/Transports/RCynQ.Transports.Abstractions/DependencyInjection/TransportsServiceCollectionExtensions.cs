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

        // The messages follow the language of the host; a host that registered no localizer gets the default language.
        services.TryAddSingleton<RCynQ.Localization.ILocalizer>(_ => new RCynQ.Localization.Localizer(RCynQ.Localization.LanguageCatalog.LoadEmbedded()));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher>(NullEventPublisher.Instance);
        services.TryAddSingleton<TransportLinkFactory>();
        services.TryAddSingleton<ITransportLinkFactory>(provider => provider.GetRequiredService<TransportLinkFactory>());
        services.TryAddSingleton<ILinkManager, LinkManager>();
        return services;
    }
}
