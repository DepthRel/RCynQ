namespace RCynQ.Dbs.SQLite.DbMigrator.Arguments;

/// <summary>
/// Parsed command-line arguments of the migrator.
/// </summary>
/// <param name="ConnectionString">The connection string to migrate, or <see langword="null"/>.</param>
/// <param name="ShowHelp">Whether help was requested.</param>
/// <param name="ListOnly">Whether only applied versions should be listed.</param>
/// <param name="Error">A usage error, or <see langword="null"/>.</param>
public sealed record MigratorArguments(string? ConnectionString, bool ShowHelp, bool ListOnly, string? Error)
{
    /// <summary>
    /// The usage text.
    /// </summary>
    public const string USAGE =
        """
        RCynQ.Dbs.SQLite.DbMigrator - applies RCynQ SQLite schema migrations.

        Usage:
          RCynQ.Dbs.SQLite.DbMigrator --connection "Data Source=/path/rcynq.db"
          RCynQ.Dbs.SQLite.DbMigrator --database /path/rcynq.db
          RCynQ.Dbs.SQLite.DbMigrator "Data Source=/path/rcynq.db"

        Options:
          -c, --connection <value>  SQLite connection string.
          -d, --database <path>     SQLite database file (shortcut for Data Source=<path>).
          -l, --list                List applied migrations instead of migrating.
          -h, --help                Show this help.

        Exit codes: 0 success, 1 migration error, 2 usage error.
        """;

    /// <summary>
    /// Parses the command-line arguments.
    /// </summary>
    /// <param name="args">The arguments.</param>
    /// <returns>The parsed arguments; <see cref="Error"/> is set for invalid input.</returns>
    public static MigratorArguments Parse(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        string? connection = null;
        var list = false;

        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help" or "/?":
                    return new MigratorArguments(null, true, false, null);
                case "-l" or "--list":
                    list = true;
                    break;
                case "-c" or "--connection" or "-d" or "--database":
                    if (i + 1 >= args.Count || string.IsNullOrWhiteSpace(args[i + 1]))
                    {
                        return new MigratorArguments(null, false, false, $"Option '{arg}' requires a value.");
                    }

                    if (connection is not null)
                    {
                        return new MigratorArguments(null, false, false, "The database was specified more than once.");
                    }

                    var value = args[++i];
                    connection = arg is "-d" or "--database" ? $"Data Source={value}" : value;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        return new MigratorArguments(null, false, false, $"Unknown option '{arg}'.");
                    }

                    if (connection is not null)
                    {
                        return new MigratorArguments(null, false, false, "The database was specified more than once.");
                    }

                    connection = arg;
                    break;
            }
        }

        return connection is null
            ? new MigratorArguments(null, false, false, "A connection string or database path is required.")
            : new MigratorArguments(connection, false, list, null);
    }
}
