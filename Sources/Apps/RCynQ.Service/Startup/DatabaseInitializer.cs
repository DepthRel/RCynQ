using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Migrations.SQLite.Runner;

namespace RCynQ.Service.Startup;

/// <summary>
/// Applies pending SQLite migrations on start when <see cref="SyncBridgeOptions.AutoMigrate"/> is enabled.
/// </summary>
public sealed class DatabaseInitializer : IHostedService
{
    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options;

    /// <summary>The application data factory.</summary>
    private readonly IAppDataFactory _appData;

    /// <summary>The logger.</summary>
    private readonly ILogger<DatabaseInitializer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializer"/> class.
    /// </summary>
    /// <param name="options">The node options.</param>
    /// <param name="appData">The application data factory.</param>
    /// <param name="logger">The logger.</param>
    public DatabaseInitializer(SyncBridgeOptions options, IAppDataFactory appData, ILogger<DatabaseInitializer> logger)
    {
        _options = options;
        _appData = appData;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var paths = _appData.Create();
        if (!_options.AutoMigrate)
        {
            _logger.LogInformation("Automatic migrations are disabled; database {Database} must be migrated with RCynQ.Dbs.SQLite.DbMigrator.", paths.DatabasePath);
            return Task.CompletedTask;
        }

        var applied = SqliteDatabaseMigrator.MigrateUp(paths.ConnectionString);
        _logger.LogInformation(applied.Count == 0 ? "Database {Database} is up to date." : "Applied migrations {Versions} to {Database}.",
            applied.Count == 0 ? paths.DatabasePath : string.Join(", ", applied), paths.DatabasePath);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
