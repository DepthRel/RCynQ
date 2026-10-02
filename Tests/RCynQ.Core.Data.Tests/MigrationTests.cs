using FluentAssertions;
using Microsoft.Data.Sqlite;
using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Core.Data.Tests.Support;
using RCynQ.Dbs.SQLite.DbMigrator;
using RCynQ.Dbs.SQLite.DbMigrator.Arguments;

namespace RCynQ.Core.Data.Tests;

/// <summary>
/// Integration tests for the FluentMigrator schema and the migrator executable.
/// </summary>
public class MigrationTests
{
    /// <summary>
    /// Verifies that the initial migration creates every table and is idempotent.
    /// </summary>
    [Fact]
    public void InitialSchema_Creates_All_Tables_And_Is_Idempotent()
    {
        using var database = new TestDatabase();

        using (var connection = new SqliteConnection(database.Paths.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table'";
            var tables = new List<string>();
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    tables.Add(reader.GetString(0));
                }
            }

            tables.Should().Contain(new[] { "Nodes", "ChainLinks", "Peers", "Snapshots", "PatchEnvelopes", "SyncHistory", "Secrets", "DiagnosticRuns" });
        }

        SqliteDatabaseMigrator.MigrateUp(database.Paths.ConnectionString).Should().BeEmpty();
        SqliteDatabaseMigrator.GetAppliedVersions(database.Paths.ConnectionString).Should().Equal(1L, 2L, 3L, 4L, 5L);
    }

    /// <summary>
    /// Verifies that the migrator executable migrates a new database and reports usage errors.
    /// </summary>
    [Fact]
    public void Migrator_Program_Runs_And_Validates_Arguments()
    {
        using var database = new TestDatabase();
        var freshDb = Path.Combine(database.Root, "fresh", "new.db");

        Program.Main(new[] { "--database", freshDb }).Should().Be(0);
        File.Exists(freshDb).Should().BeTrue();
        SqliteDatabaseMigrator.GetAppliedVersions($"Data Source={freshDb}").Should().Equal(1L, 2L, 3L, 4L, 5L);

        Program.Main(new[] { "--help" }).Should().Be(0);
        Program.Main(Array.Empty<string>()).Should().Be(2);
        Program.Main(new[] { "--bogus" }).Should().Be(2);
        SqliteConnection.ClearAllPools();
    }

    /// <summary>
    /// Verifies argument parsing.
    /// </summary>
    [Fact]
    public void MigratorArguments_Parse()
    {
        MigratorArguments.Parse(new[] { "-c", "Data Source=a.db" }).ConnectionString.Should().Be("Data Source=a.db");
        MigratorArguments.Parse(new[] { "-d", "a.db", "--list" }).Should().Be(new MigratorArguments("Data Source=a.db", false, true, null));
        MigratorArguments.Parse(new[] { "Data Source=b.db" }).ConnectionString.Should().Be("Data Source=b.db");
        MigratorArguments.Parse(new[] { "-c" }).Error.Should().Contain("requires a value");
        MigratorArguments.Parse(new[] { "a", "b" }).Error.Should().Contain("more than once");
        MigratorArguments.Parse(new[] { "/?" }).ShowHelp.Should().BeTrue();
    }
}
