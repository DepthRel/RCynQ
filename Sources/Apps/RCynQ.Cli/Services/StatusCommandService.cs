using System.Text;
using RCynQ.Cli.Output;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Sync.Services;

namespace RCynQ.Cli.Services;

/// <summary>
/// Builds the <c>status</c> report: node, links, synchronization state and recent patches.
/// </summary>
public sealed class StatusCommandService
{
    /// <summary>The patch service.</summary>
    private readonly IPatchService _patches;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommandService"/> class.
    /// </summary>
    /// <param name="patches">The patch service.</param>
    /// <param name="links">The link repository.</param>
    public StatusCommandService(IPatchService patches, IChainLinkRepository links)
    {
        _patches = patches;
        _links = links;
    }

    /// <summary>
    /// Builds the report.
    /// </summary>
    /// <param name="recentCount">The number of recent patches to include.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report text.</returns>
    public async Task<string> BuildAsync(int recentCount = 5, CancellationToken cancellationToken = default)
    {
        var status = await _patches.GetStateAsync(cancellationToken);
        var links = await _links.GetAllAsync(cancellationToken);
        var recent = await _patches.GetRecentAsync(recentCount, cancellationToken);
        var text = new StringBuilder();

        text.Append("Node:           ").Append(status.NodeId).Append(" (").Append(status.Role).Append(") ").Append(status.Name).Append('\n');
        if (status.Role != Core.Enums.NodeRole.Mediator)
        {
            text.Append("Sync base:      ").Append(DescribeSyncBase(status.SyncBase)).Append('\n');
            text.Append("Local state:    ").Append(TextFormat.Hash(status.State.LocalStateHash)).Append('\n');
            text.Append("Base state:     ").Append(TextFormat.Hash(status.State.SyncBaseHash)).Append('\n');
            text.Append("Last sent:      ").Append(TextFormat.Time(status.State.LastSentAtUtc)).Append('\n');
            text.Append("Last received:  ").Append(TextFormat.Time(status.State.LastReceivedAtUtc)).Append('\n');
            text.Append("Pending:        ").Append(status.PendingChanges.Count).Append(" file(s)\n");
            foreach (var change in status.PendingChanges.Take(20))
            {
                text.Append("  ").Append(TextFormat.Change(change.Kind)).Append(' ').Append(change.Path).Append('\n');
            }

            if (status.PendingChanges.Count > 20)
            {
                text.Append("  ... ").Append(status.PendingChanges.Count - 20).Append(" more\n");
            }

            if (status.ConflictPendingCount > 0)
            {
                text.Append("Conflicts:      ").Append(status.ConflictPendingCount).Append(" patch(es) wait for 'patch resolve <id> --policy ...'\n");
            }
        }

        text.Append("Links:          ").Append(links.Count == 0 ? "none" : links.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append('\n');
        if (links.Count > 0)
        {
            var table = new ConsoleTable("Pos", "Name", "Role", "Address", "Transport");
            foreach (var link in links)
            {
                var transport = link.FallbackTransports.Count == 0 ? link.Transport.ToString() : $"{link.Transport} (+{string.Join(",", link.FallbackTransports)})";
                table.AddRow(link.Priority.ToString(System.Globalization.CultureInfo.InvariantCulture), link.Name, link.Peer.Role.ToString(), link.Peer.Ssh.JumpSpec, transport);
            }

            text.Append(table.Render("  "));
        }

        if (recent.Count > 0)
        {
            text.Append("Recent patches:\n");
            var table = new ConsoleTable("Created", "Direction", "Status", "Files", "Id");
            foreach (var patch in recent)
            {
                table.AddRow(TextFormat.Time(patch.CreatedAtUtc), patch.Direction.ToString(), patch.Status.ToString(), patch.Manifest.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), patch.Id.ToString());
            }

            text.Append(table.Render("  "));
        }

        return text.ToString();
    }

    /// <summary>
    /// Describes where the sync base comes from, or why there is none.
    /// </summary>
    /// <param name="syncBase">The sync base status.</param>
    /// <returns>The text.</returns>
    private static string DescribeSyncBase(SyncBaseStatus syncBase) => syncBase.Mode switch
    {
        SyncBaseMode.Git => $"Git {syncBase.Branch ?? "HEAD"} @ {TextFormat.Hash(syncBase.Commit)}",
        SyncBaseMode.Manual => "manual snapshot",
        _ => $"not ready: {syncBase.Problem}" + (syncBase.CanCreateManual ? " (or run 'patch baseline')" : string.Empty)
    };
}
