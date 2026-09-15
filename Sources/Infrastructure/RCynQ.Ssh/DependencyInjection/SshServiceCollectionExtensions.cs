using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Ssh;
using RCynQ.Core.Config;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Forwarding;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.AuthorizedKeys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Ssh.Network;

namespace RCynQ.Ssh.DependencyInjection;

/// <summary>
/// Registers the SSH layer in a service collection.
/// </summary>
public static class SshServiceCollectionExtensions
{
    /// <summary>
    /// Registers SSH command building, execution, forwarding, key and host key services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>Requires <see cref="SyncBridgeOptions"/> and an <see cref="IProcessRunner"/> to be registered.</remarks>
    public static IServiceCollection AddRCynQSsh(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(provider => provider.GetRequiredService<SyncBridgeOptions>().Ssh);
        services.TryAddSingleton<SshCommandBuilder>();
        services.TryAddSingleton<ISshExecutor, SystemSshRunner>();
        services.TryAddSingleton<ITcpProbe, SocketTcpProbe>();
        services.TryAddSingleton<IPortAllocator, LoopbackPortAllocator>();
        services.TryAddSingleton<ILocalForwardManager, ProcessLocalForwardManager>();
        services.TryAddSingleton<SshKeyGenerator>();
        services.TryAddSingleton<AuthorizedKeysManager>();
        services.TryAddSingleton<HostKeyVerifier>();
        return services;
    }
}
