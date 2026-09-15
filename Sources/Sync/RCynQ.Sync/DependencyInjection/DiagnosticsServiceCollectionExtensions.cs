using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Ssh.Keys;
using RCynQ.Sync.Diagnostics.AutoFix;
using RCynQ.Sync.Diagnostics.Checks;
using RCynQ.Sync.Diagnostics.Endpoint;
using RCynQ.Sync.Diagnostics.Runner;
using RCynQ.Transports.Abstractions;

namespace RCynQ.Sync.DependencyInjection;

/// <summary>
/// Registers diagnostics.
/// </summary>
public static class DiagnosticsServiceCollectionExtensions
{
    /// <summary>
    /// Registers all checks, the runner, the safe auto-fixer and its handlers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    public static IServiceCollection AddRCynQDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<ILocalEndpointProbe, HttpLocalEndpointProbe>();
        services.TryAddSingleton<ILocalEndpointController, ExternalLocalEndpointController>();

        foreach (var check in new[]
        {
            typeof(GitInstalledCheck), typeof(GitVersionCheck), typeof(SshClientCheck), typeof(SshdListeningCheck),
            typeof(WorkdirWritableCheck), typeof(RepoValidCheck), typeof(SnapshotIntegrityCheck), typeof(NeighborReachabilityCheck),
            typeof(TransportAvailabilityCheck), typeof(AuthorizedKeysFormatCheck), typeof(PatchEndpointLocalCheck), typeof(ChainLinkConfigCheck)
        })
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IDiagnosticCheck), check));
        }

        foreach (var handler in new[] { typeof(CreateDirectoryFixHandler), typeof(StartTransportFixHandler), typeof(AddAuthorizedKeyFixHandler), typeof(RestartLocalEndpointFixHandler) })
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(IAutoFixHandler), handler));
        }

        services.TryAddSingleton<IDiagnosticsRunner>(provider => ActivatorUtilities.CreateInstance<DiagnosticsRunner>(
            provider,
            provider.GetService<IDiagnosticRunRepository>() ?? (object)NoRunRepository.Instance));
        services.TryAddSingleton<IAutoFixer, SafeAutoFixer>();
        return services;
    }

    /// <summary>
    /// A run repository that stores nothing, used when no persistence is registered.
    /// </summary>
    private sealed class NoRunRepository : IDiagnosticRunRepository
    {
        /// <summary>Gets the shared instance.</summary>
        public static NoRunRepository Instance { get; } = new();

        /// <inheritdoc />
        public Task AddAsync(DiagnosticsEvent run, CancellationToken cancellationToken = default) => Task.CompletedTask;

        /// <inheritdoc />
        public Task<IReadOnlyList<DiagnosticsEvent>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DiagnosticsEvent>>(Array.Empty<DiagnosticsEvent>());
    }
}
