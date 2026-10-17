using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Backups;
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

        // The messages follow the language of the host; a host that registered no localizer gets the default language.
        services.TryAddSingleton<RCynQ.Localization.ILocalizer>(_ => new RCynQ.Localization.Localizer(RCynQ.Localization.LanguageCatalog.LoadEmbedded()));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IEventPublisher>(NullEventPublisher.Instance);
        // The catalog is the one place that knows which repository the node works with now.
        services.TryAddSingleton<Repositories.RepositoryCatalog>();
        services.TryAddSingleton<RCynQ.Core.Config.IActiveRepository>(provider => provider.GetRequiredService<Repositories.RepositoryCatalog>());
        services.TryAddSingleton<Repositories.PeerRepositoryTracker>();
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<Repositories.PeerRepositoryClient>(
            provider,
            new PeerLinkFactory(
                new SocketsHttpHandler { UseProxy = false },
                provider.GetRequiredService<ILocalForwardManager>(),
                hostKeys: provider.GetService<RCynQ.Ssh.Keys.HostKeys.IPinnedHostKeyGuard>())));
        services.TryAddSingleton<Repositories.RepositoryService>();
        services.TryAddSingleton<SyncPaths>();

        // One lock file per node, in its data directory: the service and the CLI of the same node take turns on the
        // working tree and the sync base instead of changing them at the same time.
        services.TryAddSingleton(provider => new WorkingTreeLock(
            Path.Combine(provider.GetRequiredService<IAppDataFactory>().Create().Root, WorkingTreeLock.LOCK_FILE_NAME)));
        services.TryAddSingleton<ISignatureService, EcdsaSignatureService>();
        services.TryAddSingleton(provider => new ReplayGuardOptions
        {
            MaxAge = TimeSpan.FromMinutes(provider.GetRequiredService<SyncBridgeOptions>().PatchMaxAgeMinutes)
        });
        services.TryAddSingleton<ReplayGuard>();
        services.TryAddSingleton<LinkTokenProvider>();
        services.TryAddSingleton<ConflictDetector>();
        services.TryAddSingleton<ContentOverwriteApplier>();
        services.TryAddSingleton<SyncBaseUpdater>();
        services.TryAddSingleton<IPatchApplier>(provider => provider.GetRequiredService<ContentOverwriteApplier>());
        services.TryAddSingleton<BackupService>();
        services.TryAddSingleton<GitMergeApplier>();
        services.TryAddSingleton<IConflictResolver, DefaultConflictResolver>();
        services.TryAddSingleton<StateTracker>();
        services.TryAddSingleton<SyncBase.SyncBaseMetadataStore>();
        services.TryAddSingleton<SyncBase.IGitSyncBaseLocator, SyncBase.GitSyncBaseLocator>();
        services.TryAddSingleton<SyncBase.SyncBaseManager>();
        services.TryAddSingleton<PatchOutcomeRecorder>();
        services.TryAddSingleton<PatchDisplayDiffStore>();
        services.TryAddSingleton(new HttpPatchSenderOptions());
        services.TryAddSingleton<IPatchSender>(provider => ActivatorUtilities.CreateInstance<HttpPatchSender>(
            provider,
            new SocketsHttpHandler { UseProxy = false }));
        services.TryAddSingleton<PatchAdmission>();
        services.TryAddSingleton<IncomingPatchApplier>();
        services.TryAddSingleton<IPatchReceiver, DefaultPatchReceiver>();
        services.TryAddSingleton<PatchSendOperation>();
        services.TryAddSingleton<PatchResolveOperation>();
        services.TryAddSingleton<IPatchService, PatchService>();
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<Configuration.NodeConfigurationTransfer>(
            provider,
            NodeCertificateFingerprint(provider)));
        services.TryAddSingleton<Configuration.ConfiguredLinkStore>();
        services.TryAddSingleton<INetworkInterfaceSource, SystemNetworkInterfaceSource>();
        services.TryAddSingleton<INodeEnvironmentProbe, NodeEnvironmentProbe>();
        services.TryAddSingleton<Configuration.Access.PeerAccessService>();

        // Invitations and enrollment; the host names the command the SSH server runs (IEnrollmentCommandSource).
        services.TryAddSingleton<Enrollment.LocalCardBuilder>();
        services.TryAddSingleton<Enrollment.RelayTicketStore>();
        services.TryAddSingleton<Enrollment.TicketIssuer>();
        services.TryAddSingleton<Enrollment.InvitationService>();
        services.TryAddSingleton<Enrollment.EnrollmentService>();
        services.TryAddSingleton<Enrollment.JoinService>();
        services.TryAddSingleton<Enrollment.IEnrollmentChannel>(provider => ActivatorUtilities.CreateInstance<Enrollment.EnrollmentChannel>(
            provider,
            new PeerLinkFactory(
                new SocketsHttpHandler { UseProxy = false },
                provider.GetRequiredService<ILocalForwardManager>(),
                hostKeys: provider.GetService<RCynQ.Ssh.Keys.HostKeys.IPinnedHostKeyGuard>())));
        services.TryAddSingleton<GitChannel.GitBundleService>();
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<GitChannel.GitBundleClient>(
            provider,
            new PeerLinkFactory(
                new SocketsHttpHandler { UseProxy = false },
                provider.GetRequiredService<ILocalForwardManager>(),
                hostKeys: provider.GetService<RCynQ.Ssh.Keys.HostKeys.IPinnedHostKeyGuard>())));
        services.TryAddSingleton<GitChannel.LegacyRemoteMigration>();
        services.TryAddSingleton<GitChannel.GitChannelService>();
        services.TryAddSingleton(new Routing.RouteCheckOptions());
        services.TryAddSingleton<Routing.ILinkRouteProbe, Routing.LinkRouteProbe>();
        services.TryAddSingleton<Routing.ILinkRouteSelector, Routing.LinkRouteSelector>();
        services.TryAddSingleton(new Reachability.EndpointPingOptions());
        services.TryAddSingleton<Reachability.PeerContactRegistry>();
        services.TryAddSingleton<Reachability.PeerPingService>();
        services.TryAddSingleton(provider => ActivatorUtilities.CreateInstance<Reachability.EndpointPinger>(
            provider,
            new PeerLinkFactory(
                new SocketsHttpHandler { UseProxy = false },
                provider.GetRequiredService<ILocalForwardManager>(),
                hostKeys: provider.GetService<RCynQ.Ssh.Keys.HostKeys.IPinnedHostKeyGuard>())));
        services.TryAddSingleton<RCynQ.Core.Abstractions.Transports.IEndpointProbe>(provider => provider.GetRequiredService<Reachability.EndpointPinger>());
        services.TryAddSingleton<Maintenance.DataRetentionService>();
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
