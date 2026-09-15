using System.Reflection;
using FluentMigrator.Runner;

namespace RCynQ.Core.Data.Migrations.Runner;

/// <summary>
/// Describes a database provider for <see cref="DatabaseMigrator"/>: the FluentMigrator processor, the assembly with the
/// provider-specific migrations and scripts, and connection string preparation.
/// </summary>
public interface IMigrationProvider
{
    /// <summary>
    /// Gets the provider name used in messages, for example <c>SQLite</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the assembly that contains the migrations and their embedded SQL scripts.
    /// </summary>
    Assembly MigrationsAssembly { get; }

    /// <summary>
    /// Registers the FluentMigrator processor of the provider.
    /// </summary>
    /// <param name="builder">The runner builder.</param>
    void ConfigureRunner(IMigrationRunnerBuilder builder);

    /// <summary>
    /// Validates and normalizes a connection string and prepares the database location if necessary.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The normalized connection string.</returns>
    /// <exception cref="ArgumentException">The connection string is invalid.</exception>
    string PrepareConnectionString(string connectionString);
}
