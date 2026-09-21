using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Context;
using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Core.Data.Storage;

namespace RCynQ.Core.Data.Tests.Support;

/// <summary>
/// A migrated SQLite database in a temporary directory, deleted on dispose.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TestDatabase"/> class and applies all migrations.
    /// </summary>
    public TestDatabase()
    {
        Root = Path.Combine(Path.GetTempPath(), "rcynq-data-tests", Guid.NewGuid().ToString("N"));
        Paths = AppDataPaths.FromRoot(Root);
        AppData = new AppDataFactory(Paths);
        AppData.Create();
        SqliteDatabaseMigrator.MigrateUp(Paths.ConnectionString);

        var options = new DbContextOptionsBuilder<RCynQDbContext>().UseSqlite(Paths.ConnectionString).Options;
        ContextFactory = new PooledDbContextFactory<RCynQDbContext>(options);
    }

    /// <summary>Gets the temporary root directory.</summary>
    public string Root { get; }

    /// <summary>Gets the application data paths.</summary>
    public AppDataPaths Paths { get; }

    /// <summary>Gets the application data factory.</summary>
    public IAppDataFactory AppData { get; }

    /// <summary>Gets the EF Core context factory.</summary>
    public IDbContextFactory<RCynQDbContext> ContextFactory { get; }

    /// <summary>
    /// Releases pooled SQLite connections and deletes the temporary directory.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Best effort cleanup of a temporary directory.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort cleanup of a temporary directory.
        }
    }
}
