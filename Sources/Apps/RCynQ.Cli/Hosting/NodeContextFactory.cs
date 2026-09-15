using CliFx.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.DependencyInjection;
using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Core.Exceptions;
using RCynQ.Git.DependencyInjection;
using RCynQ.Ssh.DependencyInjection;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.DependencyInjection;
using RCynQ.Transports.Abstractions.DependencyInjection;
using RCynQ.Transports.AmneziaWG.DependencyInjection;
using RCynQ.Transports.Direct.DependencyInjection;

namespace RCynQ.Cli.Hosting;

/// <summary>
/// Default <see cref="INodeContextFactory"/>: the CLI works headless, without a running service, on the same data.
/// </summary>
/// <remarks>
/// Pending migrations are applied when <see cref="SyncBridgeOptions.AutoMigrate"/> is enabled, and the local node and
/// the configured links are stored, exactly as the service does on start.
/// </remarks>
public sealed class NodeContextFactory : INodeContextFactory
{
    /// <inheritdoc />
    public async Task<NodeContext> CreateAsync(string? configPath, CancellationToken cancellationToken)
    {
        var path = ConfigurationFileEditor.ResolvePath(configPath);
        SyncBridgeOptions options;
        try
        {
            options = await ConfigurationFileEditor.LoadAsync(path, cancellationToken);
        }
        catch (SyncBridgeException ex)
        {
            throw new CommandException(ex.Message, 2);
        }

        var errors = SyncBridgeOptionsValidator.Validate(options);
        if (errors.Count > 0)
        {
            throw new CommandException($"Invalid configuration in '{path}':{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}", 2);
        }

        ConfigLocations.ApplyStorageDefaults(options, path, ConfigLocations.FindPortableRoot());
        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(node);
        services.AddRCynQData()
            .AddRCynQGit()
            .AddRCynQSsh()
            .AddRCynQTransports()
            .AddDirectTransport()
            .AddAmneziaWgTransport()
            .AddRCynQSync()
            .AddRCynQDiagnostics()
            .AddCliCommandServices();

        var provider = services.BuildServiceProvider();
        var paths = provider.GetRequiredService<IAppDataFactory>().Create();
        if (options.AutoMigrate)
        {
            SqliteDatabaseMigrator.MigrateUp(paths.ConnectionString);
        }

        await provider.GetRequiredService<INodeRepository>().UpsertAsync(node, cancellationToken);
        var links = provider.GetRequiredService<IChainLinkRepository>();
        foreach (var link in SyncBridgeOptionsValidator.ToChainLinks(options))
        {
            await links.UpsertAsync(link, cancellationToken);
        }

        return new NodeContext(provider, options, path);
    }
}
