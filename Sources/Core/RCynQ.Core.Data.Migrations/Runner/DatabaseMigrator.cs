using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace RCynQ.Core.Data.Migrations.Runner;

/// <summary>
/// Provider-agnostic FluentMigrator runner.
/// </summary>
/// <remarks>
/// The provider supplies the processor and the migrations assembly; the runner scans that assembly for migrations and
/// embedded SQL scripts and records applied versions in the FluentMigrator version table.
/// </remarks>
public sealed class DatabaseMigrator
{
    /// <summary>
    /// The database provider.
    /// </summary>
    private readonly IMigrationProvider _provider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseMigrator"/> class.
    /// </summary>
    /// <param name="provider">The database provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <see langword="null"/>.</exception>
    public DatabaseMigrator(IMigrationProvider provider)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
    }

    /// <summary>
    /// Applies all pending migrations.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The versions applied by this call, ascending; empty when the database was up to date.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public IReadOnlyList<long> MigrateUp(string connectionString)
    {
        using var provider = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = provider.CreateScope();
        var versions = scope.ServiceProvider.GetRequiredService<IVersionLoader>();
        var before = versions.VersionInfo.AppliedMigrations().ToHashSet();

        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();

        versions.LoadVersionInfo();
        return versions.VersionInfo.AppliedMigrations().Where(version => !before.Contains(version)).Order().ToArray();
    }

    /// <summary>
    /// Rolls the database back to the given version.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="version">The version to keep; later migrations are reverted.</param>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public void MigrateDown(string connectionString, long version)
    {
        using var provider = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateDown(version);
    }

    /// <summary>
    /// Lists the applied versions.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The applied versions, ascending.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    public IReadOnlyList<long> GetAppliedVersions(string connectionString)
    {
        using var provider = BuildServices(_provider.PrepareConnectionString(connectionString));
        using var scope = provider.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IVersionLoader>().VersionInfo.AppliedMigrations().Order().ToArray();
    }

    /// <summary>
    /// Builds the FluentMigrator services for a connection string.
    /// </summary>
    /// <param name="connectionString">The prepared connection string.</param>
    /// <returns>The service provider.</returns>
    private ServiceProvider BuildServices(string connectionString)
        => new ServiceCollection()
            .AddLogging()
            .AddFluentMigratorCore()
            .ConfigureRunner(runner =>
            {
                _provider.ConfigureRunner(runner);
                runner.WithGlobalConnectionString(connectionString)
                    .ScanIn(_provider.MigrationsAssembly).For.All();
            })
            .BuildServiceProvider(validateScopes: false);
}
