using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Git.Repository;
using RCynQ.Localization;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.History;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Learns on a Workstation what became of commits that waited on the Origin: a Fetch shows whether the Origin applied them.
/// </summary>
/// <remarks>
/// The Origin applies or declines a waiting delivery by the hand of its user, and does not tell the Workstation. After a
/// Fetch, a delivery whose last commit is on the branch of the Origin was applied; one that is not there although a later
/// delivery of the same branch was applied never will be.
/// </remarks>
public sealed class OutgoingCommitTracker
{
    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The repository service.</summary>
    private readonly IGitRepositoryService _git;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutgoingCommitTracker"/> class.
    /// </summary>
    /// <param name="patches">The patch repository.</param>
    /// <param name="git">The repository service.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    public OutgoingCommitTracker(IPatchRepository patches, IGitRepositoryService git, PatchOutcomeRecorder outcomes)
    {
        _patches = patches;
        _git = git;
        _outcomes = outcomes;
    }

    /// <summary>
    /// Closes the waiting deliveries whose fate the branches fetched from the Origin tell.
    /// </summary>
    /// <param name="workdir">The working tree.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the deliveries are looked at.</returns>
    public async Task CloseDeliveredAsync(string workdir, CancellationToken cancellationToken = default)
    {
        var waiting = (await _patches.GetByStatusAsync(PatchDirection.Outgoing, PatchStatus.Waiting, cancellationToken))
            .Where(patch => patch.Commits is not null)
            .ToArray();
        if (waiting.Length == 0)
        {
            return;
        }

        var applied = (await _patches.GetByStatusAsync(PatchDirection.Outgoing, PatchStatus.Applied, cancellationToken))
            .Where(patch => patch.Commits is not null)
            .ToArray();
        foreach (var patch in waiting)
        {
            var commits = patch.Commits!;
            var origin = await _git.ResolveCommitAsync(workdir, $"refs/remotes/{GitChannelService.REMOTE_NAME}/{commits.Branch}", cancellationToken);
            if (origin is not null && await _git.IsAncestorAsync(workdir, commits.TipCommit, origin, cancellationToken))
            {
                await _outcomes.RecordAsync(patch, PatchApplyResult.Applied(patch.Id, commits.Commits.Count), LocalizedText.Encode("Commits.AppliedLater", commits.Commits.Count), cancellationToken);
            }
            else if (applied.Any(later => later.Commits!.Branch == commits.Branch && later.CreatedAtUtc > patch.CreatedAtUtc))
            {
                await _outcomes.RecordAsync(patch, PatchApplyResult.Rejected(patch.Id, LocalizedText.Encode("Commits.NotAppliedOnOrigin")), null, cancellationToken);
            }
        }
    }
}
