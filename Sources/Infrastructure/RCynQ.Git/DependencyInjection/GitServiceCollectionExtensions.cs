using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Git.Commands;
using RCynQ.Git.Commits;
using RCynQ.Git.Hashing;
using RCynQ.Git.Manifest;
using RCynQ.Git.Patches;
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
    /// Registers process execution, path handling, hashing, manifest (with .gitignore support), diff, patch building and Git channel services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection.</returns>
    /// <remarks>Requires <see cref="SyncBridgeOptions"/> to be registered.</remarks>
    public static IServiceCollection AddRCynQGit(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // The messages follow the language of the host; a host that registered no localizer gets the default language.
        services.TryAddSingleton<RCynQ.Localization.ILocalizer>(_ => new RCynQ.Localization.Localizer(RCynQ.Localization.LanguageCatalog.LoadEmbedded()));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IProcessRunner, SystemProcessRunner>();
        services.TryAddSingleton<IPathUtility, PathUtility>();
        services.TryAddSingleton<IStateHasher, DefaultStateHasher>();
        services.TryAddSingleton(provider =>
        {
            var options = provider.GetRequiredService<SyncBridgeOptions>();

            // The configured names add to the built-in ones here, not only where the options are loaded: the service binds
            // them (appending to the defaults) while the CLI deserializes the file (replacing them), and both must scan
            // the same working tree with the same filter. The filter trims and deduplicates the names itself.
            return new SnapshotFilter(
                SyncBridgeOptions.DefaultExcludedDirectories.Concat(options.ExcludedDirectories ?? Enumerable.Empty<string>()),
                options.RespectGitIgnore);
        });
        services.TryAddSingleton<FileHashCache>();
        services.TryAddSingleton<IFileManifestBuilder, DefaultFileManifestBuilder>();
        services.TryAddSingleton(provider => new GitOptions { GitExecutable = provider.GetRequiredService<SyncBridgeOptions>().Ssh.GitExecutable });
        services.TryAddSingleton<IGitCommandRunner, GitCommandRunner>();
        services.TryAddSingleton<IGitDiffBuilder, GitDiffBuilder>();
        services.TryAddSingleton<IPatchDiffBuilder, PatchDiffBuilder>();
        services.TryAddSingleton<IPatchBuilder, DefaultPatchBuilder>();
        services.TryAddSingleton<PatchEnvelopeSerializer>();
        services.TryAddSingleton<IGitRepositoryService, GitRepositoryService>();
        services.TryAddSingleton<IGitCommitService, GitCommitService>();
        return services;
    }
}
