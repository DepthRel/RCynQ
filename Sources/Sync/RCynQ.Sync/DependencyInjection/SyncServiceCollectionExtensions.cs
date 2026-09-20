using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
using RCynQ.Sync.Baseline;
using RCynQ.Sync.Configuration.Probing;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.Services;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.DependencyInjection;

/// <summary>
/// Registers the patch channel in a service collection.
/// </summary>
public static class SyncServiceCollectionExtensions
{
    /// <summary>
    /// Registers signing, replay protection, conflict handling, state tracking, sender, receiver and patch service.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>
    /// Requires <see cref="SyncBridgeOptions"/>, <see cref="NodeConfig"/>, the data layer, the Git layer and the SSH layer.
    /// A no-op <see cref="IEventPublisher"/> is registered unless one was registered before.
    /// </remarks>
    public static IServiceCollection AddRCynQSync(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher>(NullEventPublisher.Instance);
        services.TryAddSingleton<SyncPaths>();
        services.TryAddSingleton<WorkingTreeLock>();
        services.TryAddSingleton<ISignatureService, EcdsaSignatureService>();
        services.TryAddSingleton(provider => new ReplayGuardOptions
        {
            MaxAge = TimeSpan.FromMinutes(provider.GetRequiredService<SyncBridgeOptions>().PatchMaxAgeMinutes)
        });
        services.TryAddSingleton<ReplayGuard>();
        services.TryAddSingleton<LinkTokenProvider>();
        services.TryAddSingleton<ConflictDetector>();
        services.TryAddSingleton<BundleOverwriteApplier>();
        services.TryAddSingleton<GitPatchApplier>();
        services.TryAddSingleton<IPatchApplier>(provider => provider.GetRequiredService<BundleOverwriteApplier>());
        services.TryAddSingleton<BackupService>();
        services.TryAddSingleton<IConflictResolver, DefaultConflictResolver>();
        services.TryAddSingleton<StateTracker>();
        services.TryAddSingleton<BaselineService>();
        services.TryAddSingleton<PatchOutcomeRecorder>();
        services.TryAddSingleton(new HttpPatchSenderOptions());
        services.TryAddSingleton<IPatchSender>(provider => ActivatorUtilities.CreateInstance<HttpPatchSender>(
            provider,
            new SocketsHttpHandler { UseProxy = false }));
        services.TryAddSingleton<IPatchReceiver, DefaultPatchReceiver>();
        services.TryAddSingleton<IPatchService, PatchService>();
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<Configuration.NodeConfigurationTransfer>(
            provider,
            NodeCertificateFingerprint(provider)));
        services.TryAddSingleton<INetworkInterfaceSource, SystemNetworkInterfaceSource>();
        services.TryAddSingleton<INodeEnvironmentProbe, NodeEnvironmentProbe>();
        services.TryAddSingleton<Configuration.Access.PeerAccessService>();
        services.TryAddSingleton<GitChannel.GitChannelService>();
        return services;
    }

    /// <summary>
    /// Builds the accessor of this node's TLS certificate fingerprint, published in its export file.
    /// </summary>
    /// <param name="provider">The service provider.</param>
    /// <returns>A function returning the fingerprint, or <see langword="null"/> when the node uses its SSH server.</returns>
    private static Func<string?> NodeCertificateFingerprint(IServiceProvider provider) => () =>
    {
        var options = provider.GetRequiredService<SyncBridgeOptions>();
        if (options.Endpoint.Mode != EndpointMode.Direct)
        {
            return null;
        }

        using var certificate = Endpoint.NodeCertificateStore.GetOrCreate(
            provider.GetRequiredService<IAppDataFactory>().Create(),
            options.NodeId);
        return Endpoint.NodeCertificateStore.Fingerprint(certificate);
    };
}
