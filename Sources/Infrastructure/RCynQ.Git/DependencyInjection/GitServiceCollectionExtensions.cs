using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Git.Commands;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Git.Patches.Diff;
using RCynQ.Git.Patches.Serialization;
using RCynQ.Git.Paths;
using RCynQ.Git.Processes;
using RCynQ.Git.Repository;

namespace RCynQ.Git.DependencyInjection;

/// <summary>
/// Registers the Git layer in a service collection.
/// </summary>
public static class GitServiceCollectionExtensions
{
    /// <summary>
    /// Registers process execution, path handling, hashing, manifest, diff, bundle, patch building and Git channel services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>Requires <see cref="SyncBridgeOptions"/> to be registered.</remarks>
    public static IServiceCollection AddRCynQGit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.TryAddSingleton<IPathUtility, PathUtility>();
        services.TryAddSingleton<IStateHasher, DefaultStateHasher>();
        services.TryAddSingleton(provider => new SnapshotFilter(provider.GetRequiredService<SyncBridgeOptions>().ExcludedDirectories));
        services.TryAddSingleton<IFileManifestBuilder, DefaultFileManifestBuilder>();
        services.TryAddSingleton(provider => new GitOptions { GitExecutable = provider.GetRequiredService<SyncBridgeOptions>().Ssh.GitExecutable });
        services.TryAddSingleton<IGitCommandRunner, GitCommandRunner>();
        services.TryAddSingleton<IGitDiffBuilder, GitDiffBuilder>();
        services.TryAddSingleton<IFileBundleBuilder, FileBundleBuilder>();
        services.TryAddSingleton<IPatchBuilder, DefaultPatchBuilder>();
        services.TryAddSingleton<PatchEnvelopeSerializer>();
        services.TryAddSingleton<IGitRepositoryService, GitRepositoryService>();
        return services;
    }
}
