using System.Text;
using RCynQ.Cli.Output;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Commits;

namespace RCynQ.Cli.Services;

/// <summary>
/// Lists, sends and rolls back unsent commits of a Workstation, and applies or declines commits that wait on an Origin.
/// </summary>
public sealed class CommitCommandService
{
    /// <summary>The commit service.</summary>
    private readonly ICommitService _commits;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitCommandService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="commits">The commit service.</param>
    public CommitCommandService(ILocalizer localizer, ICommitService commits)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _commits = commits ?? throw new ArgumentNullException(nameof(commits));
    }

    /// <summary>
    /// Lists the unsent commits, newest first.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The table, or why there are no commits to send.</returns>
    public async Task<string> ListAsync(CancellationToken cancellationToken = default)
    {
        var unsent = await _commits.GetUnsentAsync(cancellationToken);
        if (unsent.Problem is not null)
        {
            return unsent.Problem + "\n";
        }

        if (unsent.Commits.Count == 0)
        {
            return _localizer.Get("Commits.NothingToSend") + "\n";
        }

        var waiting = unsent.WaitingCommits.ToHashSet(StringComparer.Ordinal);
        var table = new ConsoleTable(_localizer.Get("Sync.Time"), _localizer.Get("Commits.Hash"), _localizer.Get("Commits.Author"), _localizer.Get("Commits.Subject"));
        foreach (var commit in unsent.Commits.Reverse())
        {
            var subject = waiting.Contains(commit.Hash) ? $"[{_localizer.Get("Commits.Waiting")}] {commit.Subject}" : commit.Subject;
            table.AddRow(TextFormat.Time(commit.AuthoredAtUtc), commit.ShortHash, commit.Author, subject);
        }

        return table.Render();
    }

    /// <summary>
    /// Sends the unsent commits from the oldest one up to the given one.
    /// </summary>
    /// <param name="upTo">The last commit to send, or <see langword="null"/> for all of them.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the Origin took the commits, keeps them waiting, or there was nothing to send.</returns>
    public async Task<(string Text, bool Succeeded)> SendAsync(string? upTo, CancellationToken cancellationToken = default)
    {
        var result = await _commits.SendAsync(upTo, cancellationToken);
        var text = new StringBuilder(result.Message).Append('\n');
        if (result.PatchId is { } id)
        {
            text.Append(_localizer.Format("Commits.DeliveryId", id)).Append('\n');
        }

        return (text.ToString(), !result.Sent || result.Result?.Status is PatchStatus.Applied or PatchStatus.Waiting or PatchStatus.Pending);
    }

    /// <summary>
    /// Rolls the current branch back to a commit.
    /// </summary>
    /// <param name="target">The commit that becomes <c>HEAD</c>.</param>
    /// <param name="mode">What happens to the changes of the dropped commits.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the rollback was done.</returns>
    public async Task<(string Text, bool Succeeded)> ResetAsync(string target, CommitResetMode mode, CancellationToken cancellationToken = default)
    {
        var unsent = await _commits.GetUnsentAsync(cancellationToken);
        if (unsent.Problem is not null || unsent.HeadCommit is null)
        {
            return ((unsent.Problem ?? _localizer.Get("Commits.NothingToSend")) + "\n", false);
        }

        // A short name the user typed is matched against the commits the rollback may go back to.
        var full = new[] { unsent.OriginCommit }.Concat(unsent.Commits.Select(commit => commit.Hash))
            .FirstOrDefault(hash => hash is not null && hash.StartsWith(target.Trim(), StringComparison.OrdinalIgnoreCase)) ?? target.Trim();
        var result = await _commits.ResetAsync(full, mode, unsent.HeadCommit, cancellationToken);
        return (result.Output + "\n", result.Succeeded);
    }

    /// <summary>
    /// Applies commits that wait on this Origin.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report and whether the commits were applied.</returns>
    public async Task<(string Text, bool Succeeded)> ApplyAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        var result = await _commits.ApplyWaitingAsync(patchId, cancellationToken);
        return (Describe(patchId, result), result.Status == PatchStatus.Applied);
    }

    /// <summary>
    /// Declines commits that wait on this Origin.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The report.</returns>
    public async Task<string> DeclineAsync(Guid patchId, CancellationToken cancellationToken = default)
        => Describe(patchId, await _commits.DeclineWaitingAsync(patchId, cancellationToken));

    /// <summary>
    /// Describes the outcome of a decision on commits that wait.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>The report.</returns>
    private string Describe(Guid patchId, PatchApplyResult result)
    {
        var text = new StringBuilder(_localizer.Format("Commits.DeliveryStatus", patchId, _localizer.Get($"Patch.{result.Status}"))).Append('\n');
        if (LocalizedText.Render(result.Error, _localizer) is { } error)
        {
            text.Append(error).Append('\n');
        }

        return text.ToString();
    }
}
