using RCynQ.Core.Data.Migrations.SQLite.Runner;
using RCynQ.Dbs.SQLite.DbMigrator.Arguments;

namespace RCynQ.Dbs.SQLite.DbMigrator;

/// <summary>
/// Entry point of the RCynQ SQLite database migrator.
/// </summary>
public static class Program
{
    /// <summary>
    /// Parses the arguments and applies or lists migrations.
    /// </summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns><c>0</c> on success, <c>1</c> on a migration error, <c>2</c> on a usage error.</returns>
    public static int Main(string[] args)
    {
        var parsed = MigratorArguments.Parse(args);
        if (parsed.ShowHelp)
        {
            Console.WriteLine(MigratorArguments.Usage);
            return 0;
        }

        if (parsed.Error is not null)
        {
            Console.Error.WriteLine($"error: {parsed.Error}");
            Console.Error.WriteLine("Run with --help for usage.");
            return 2;
        }

        try
        {
            if (parsed.ListOnly)
            {
                var applied = SqliteDatabaseMigrator.GetAppliedVersions(parsed.ConnectionString!);
                Console.WriteLine(applied.Count == 0 ? "No migrations applied." : "Applied migrations: " + string.Join(", ", applied));
                return 0;
            }

            var versions = SqliteDatabaseMigrator.MigrateUp(parsed.ConnectionString!);
            Console.WriteLine(versions.Count == 0
                ? "Database is up to date."
                : "Applied migrations: " + string.Join(", ", versions));
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Migration failed: {ex.Message}");
            return 1;
        }
    }
}
