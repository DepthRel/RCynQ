using System.Globalization;
using CliFx.Attributes;
using CliFx.Exceptions;
using CliFx.Infrastructure;
using RCynQ.Cli.Hosting;
using RCynQ.Localization;
using RCynQ.Sync.Maintenance;

namespace RCynQ.Cli.Commands.Data;

/// <summary>
/// <c>rcynq data cleanup</c>: removes the finished data of the node.
/// </summary>
[Command("data cleanup", Description = "Removes finished data: old patches with their diffs and content, history, diagnostics runs, backups and temporary leftovers. Patches that wait for a conflict decision are kept.")]
public sealed class DataCleanupCommand : NodeCommandBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataCleanupCommand"/> class.
    /// </summary>
    /// <param name="contexts">The node context factory.</param>
    public DataCleanupCommand(INodeContextFactory contexts)
        : base(contexts)
    {
    }

    /// <summary>
    /// Gets or sets the age in days above which data is removed.
    /// </summary>
    [CommandOption("older-than", Description = "Remove only data older than this many days (default: 0, everything that is finished).")]
    public int OlderThanDays { get; set; }

    /// <inheritdoc />
    protected override async ValueTask ExecuteAsync(NodeContext context, IConsole console, CancellationToken cancellationToken)
    {
        if (OlderThanDays < 0)
        {
            throw new CommandException(context.Get<ILocalizer>().Get("Cli.Cleanup.NegativeAge"), 2);
        }

        var report = await context.Get<DataRetentionService>().CleanAsync(TimeSpan.FromDays(OlderThanDays), cancellationToken);
        var megabytes = (report.FreedBytes / (1024d * 1024d)).ToString("0.0", CultureInfo.InvariantCulture);
        await console.Output.WriteLineAsync(report.RemovedAnything
            ? context.Get<ILocalizer>().Format("Cli.Cleanup.Done", report.Patches, report.HistoryEntries, report.DiagnosticRuns, report.Backups, report.TemporaryItems, megabytes)
            : context.Get<ILocalizer>().Get("Cli.Cleanup.Nothing"));
    }
}
