using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Repositories;
using RCynQ.Core.Data.Storage;

namespace RCynQ.Core.Data.DependencyInjection;

/// <summary>
/// Registers the data layer in a service collection.
/// </summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the EF Core context factory, repositories, file storage and secret store.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Requires <see cref="SyncBridgeOptions"/> and <see cref="NodeConfig"/> to be registered, unless an
    /// <see cref="IAppDataFactory"/> is registered beforehand.
    /// </remarks>
    public static IServiceCollection AddRCynQData(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAppDataFactory>(provider => new AppDataFactory(provider.GetRequiredService<SyncBridgeOptions>()));
        services.AddDbContextFactory<RCynQDbContext>((provider, options) =>
            options.UseSqlite(provider.GetRequiredService<IAppDataFactory>().Create().ConnectionString));

        services.TryAddSingleton<INodeRepository, NodeRepository>();
        services.TryAddSingleton<INodeStateStore, NodeStateStore>();
        services.TryAddSingleton<ChainLinkRepository>();
        services.TryAddSingleton<IChainLinkRepository>(provider => provider.GetRequiredService<ChainLinkRepository>());
        services.TryAddSingleton<IPeerRepository>(provider => provider.GetRequiredService<ChainLinkRepository>());
        services.TryAddSingleton<IPatchRepository, PatchRepository>();
        services.TryAddSingleton<ISnapshotRepository, SnapshotRepository>();
        services.TryAddSingleton<ISyncHistoryRepository, SyncHistoryRepository>();
        services.TryAddSingleton<IDiagnosticRunRepository, DiagnosticRunRepository>();
        services.TryAddSingleton<IPatchFileStorage, PatchFileStorage>();
        services.TryAddSingleton<ISecretStore, AesGcmSecretStore>();
        return services;
    }
}
