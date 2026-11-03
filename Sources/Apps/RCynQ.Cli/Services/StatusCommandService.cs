using System.Globalization;
using System.Text;
using RCynQ.Cli.Output;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Localization;
using RCynQ.Sync.Services;

namespace RCynQ.Cli.Services;

/// <summary>
/// Builds the <c>status</c> report: node, links, synchronization state and recent patches.
/// </summary>
public sealed class StatusCommandService
{
    /// <summary>The most changed files listed one by one.</summary>
    private const int MAX_LISTED_CHANGES = 20;

    /// <summary>The patch service.</summary>
    private readonly IPatchService _patches;

    /// <summary>The link repository.</summary>
    private readonly IChainLinkRepository _links;

    /// <summary>The localizer of the output.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the output.</param>
    /// <param name="patches">The patch service.</param>
    /// <param name="links">The link repository.</param>
    public StatusCommandService(ILocalizer localizer, IPatchService patches, IChainLinkRepository links)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
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
        var fields = new List<(string Label, string Value)>
        {
            (_localizer.Get("Cli.Status.Node"), $"{status.NodeId} ({status.Role}) {status.Name}")
        };

        if (status.Role != NodeRole.Relay)
        {
            fields.Add((_localizer.Get("Cli.Status.SyncBase"), DescribeSyncBase(status.SyncBase)));
            fields.Add((_localizer.Get("Cli.Status.LocalState"), TextFormat.Hash(status.State.LocalStateHash)));
            fields.Add((_localizer.Get("Cli.Status.BaseState"), TextFormat.Hash(status.State.SyncBaseHash)));
            fields.Add((_localizer.Get("Cli.Status.LastSent"), TextFormat.Time(status.State.LastSentAtUtc)));
            fields.Add((_localizer.Get("Cli.Status.LastReceived"), TextFormat.Time(status.State.LastReceivedAtUtc)));
            fields.Add((_localizer.Get("Cli.Status.Pending"), _localizer.Format("Cli.Status.PendingFiles", status.PendingChanges.Count)));
            if (status.ConflictPendingCount > 0)
            {
                fields.Add((_localizer.Get("Cli.Status.Conflicts"), _localizer.Format("Cli.Status.ConflictsWaiting", status.ConflictPendingCount)));
            }
        }

        fields.Add((_localizer.Get("Cli.Status.Links"), links.Count == 0 ? _localizer.Get("Cli.Status.None") : links.Count.ToString(CultureInfo.InvariantCulture)));

        var width = fields.Max(field => field.Label.Length) + 3;
        var text = new StringBuilder();
        foreach (var (label, value) in fields)
        {
            text.Append((label + ":").PadRight(width)).Append(value).Append('\n');
        }

        if (status.Role != NodeRole.Relay && status.PendingChanges.Count > 0)
        {
            AppendChanges(text, status);
        }

        if (links.Count > 0)
        {
            var table = new ConsoleTable(_localizer.Get("Cli.Column.Pos"), _localizer.Get("Cli.Column.Name"), _localizer.Get("Cli.Column.Role"), _localizer.Get("Cli.Column.Address"), _localizer.Get("Cli.Column.Transport"));
            foreach (var link in links)
            {
                var transport = link.FallbackTransports.Count == 0 ? link.Transport.ToString() : $"{link.Transport} (+{string.Join(",", link.FallbackTransports)})";
                table.AddRow(link.Priority.ToString(CultureInfo.InvariantCulture), link.Name, link.Peer.Role.ToString(), link.Peer.Ssh.JumpSpec, transport);
            }

            text.Append(table.Render("  "));
        }

        if (recent.Count > 0)
        {
            text.Append(_localizer.Get("Cli.Status.RecentPatches")).Append('\n');
            var table = new ConsoleTable(_localizer.Get("Cli.Column.Created"), _localizer.Get("Cli.Column.Direction"), _localizer.Get("Cli.Column.Status"), _localizer.Get("Cli.Column.Files"), _localizer.Get("Cli.Column.Id"));
            foreach (var patch in recent)
            {
                table.AddRow(TextFormat.Time(patch.CreatedAtUtc), patch.Direction.ToString(), patch.Status.ToString(), patch.Manifest.Count.ToString(CultureInfo.InvariantCulture), patch.Id.ToString());
            }

            text.Append(table.Render("  "));
        }

        return text.ToString();
    }

    /// <summary>
    /// Lists the changed files of the working tree, the first ones by name.
    /// </summary>
    /// <param name="text">The report.</param>
    /// <param name="status">The state of the node.</param>
    private void AppendChanges(StringBuilder text, NodeSyncStatus status)
    {
        foreach (var change in status.PendingChanges.Take(MAX_LISTED_CHANGES))
        {
            text.Append("  ").Append(TextFormat.Change(change.Kind)).Append(' ').Append(change.Path).Append('\n');
        }

        if (status.PendingChanges.Count > MAX_LISTED_CHANGES)
        {
            text.Append("  ").Append(_localizer.Format("Cli.Status.More", status.PendingChanges.Count - MAX_LISTED_CHANGES)).Append('\n');
        }
    }

    /// <summary>
    /// Describes where the sync base comes from, or why there is none.
    /// </summary>
    /// <param name="syncBase">The sync base status.</param>
    /// <returns>The text.</returns>
    private string DescribeSyncBase(SyncBaseStatus syncBase) => syncBase.Mode switch
    {
        SyncBaseMode.Git => $"Git {syncBase.Branch ?? "HEAD"} @ {TextFormat.Hash(syncBase.Commit)}",
        SyncBaseMode.Manual => _localizer.Get("Cli.Status.ManualBase"),
        _ when syncBase.CanCreateManual => _localizer.Format("Cli.Status.BaseNotReadyManual", syncBase.Problem ?? string.Empty),
        _ => _localizer.Format("Cli.Status.BaseNotReady", syncBase.Problem ?? string.Empty)
    };
}
