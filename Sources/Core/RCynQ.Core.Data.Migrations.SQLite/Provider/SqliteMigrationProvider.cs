using System.Reflection;
using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using RCynQ.Core.Data.Migrations.Runner;

namespace RCynQ.Core.Data.Migrations.SQLite.Provider;

/// <summary>
/// <see cref="IMigrationProvider"/> for SQLite.
/// </summary>
public sealed class SqliteMigrationProvider : IMigrationProvider
{
    /// <inheritdoc />
    public string Name => "SQLite";

    /// <inheritdoc />
    public Assembly MigrationsAssembly => typeof(SqliteMigrationProvider).Assembly;

    /// <inheritdoc />
    public void ConfigureRunner(IMigrationRunnerBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddSQLite();
    }

    /// <inheritdoc />
    /// <remarks>Creates the directory of a file database so SQLite can create the file.</remarks>
    public string PrepareConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string must not be blank.", nameof(connectionString));
        }

        SqliteConnectionStringBuilder builder;
        try
        {
            builder = new SqliteConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Connection string is invalid: {ex.Message}", nameof(connectionString), ex);
        }

        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException("Connection string must specify 'Data Source'.", nameof(connectionString));
        }

        if (builder.DataSource != ":memory:")
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        return builder.ToString();
    }
}
