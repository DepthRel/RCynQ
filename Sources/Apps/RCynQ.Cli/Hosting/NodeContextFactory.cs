using CliFx.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.DependencyInjection;
using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Core.Exceptions;
using RCynQ.Git.DependencyInjection;
using RCynQ.Localization;
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
    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeContextFactory"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    public NodeContextFactory(ILocalizer localizer)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
    }

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
            throw new CommandException(_localizer.Describe(ex), 2);
        }

        var errors = SyncBridgeOptionsValidator.Validate(options, _localizer);
        if (errors.Count > 0)
        {
            throw new CommandException($"Invalid configuration in '{path}':{Environment.NewLine} - {string.Join(Environment.NewLine + " - ", errors)}", 2);
        }

        ConfigLocations.ApplyStorageDefaults(options, path, ConfigLocations.FindPortableRoot());
        var node = SyncBridgeOptionsValidator.ToNodeConfig(options);
        var services = new ServiceCollection();
        services.AddSingleton(options);
        services.AddSingleton(node);
        services.AddSingleton(_localizer);
        services.AddSingleton(new RCynQ.Sync.Repositories.RepositoryCatalogStorage(path));
        services.AddSingleton<RCynQ.Sync.Enrollment.IEnrollmentCommandSource>(new CliEnrollmentCommandSource(path));

        // A command ends with its answer and has nobody to tell an outcome later, so it waits for the ping of the
        // counterpart endpoint instead of reporting "being checked".
        services.AddSingleton(new RCynQ.Sync.Reachability.EndpointPingOptions { WaitForResult = true });
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
        await provider.GetRequiredService<ConfiguredLinkStore>().SeedAsync(options, cancellationToken);

        return new NodeContext(provider, options, path);
    }
}
