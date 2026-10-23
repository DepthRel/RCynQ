using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Services;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Default <see cref="ICommitService"/>: the operations of a Workstation, and the decisions on an Origin about the commits
/// that wait.
/// </summary>
public sealed class CommitService : ICommitService
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>Finds the unsent commits.</summary>
    private readonly UnsentCommitsReader _reader;

    /// <summary>Sends commits.</summary>
    private readonly CommitSendOperation _send;

    /// <summary>Rolls back commits.</summary>
    private readonly CommitResetOperation _reset;

    /// <summary>Applies deliveries of commits.</summary>
    private readonly IncomingCommitApplier _applier;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="CommitService"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="reader">Finds the unsent commits.</param>
    /// <param name="send">Sends commits.</param>
    /// <param name="reset">Rolls back commits.</param>
    /// <param name="applier">Applies deliveries of commits.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="tracker">The state tracker.</param>
    public CommitService(
        ILocalizer localizer,
        NodeConfig node,
        UnsentCommitsReader reader,
        CommitSendOperation send,
        CommitResetOperation reset,
        IncomingCommitApplier applier,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        SyncPaths paths,
        WorkingTreeLock workingTreeLock,
        StateTracker tracker)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _reader = reader;
        _send = send;
        _reset = reset;
        _applier = applier;
        _patches = patches;
        _storage = storage;
        _outcomes = outcomes;
        _paths = paths;
        _lock = workingTreeLock;
        _tracker = tracker;
    }

    /// <inheritdoc />
    public Task<UnsentCommitsResponse> GetUnsentAsync(CancellationToken cancellationToken = default) => _reader.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public Task<SendPatchResult> SendAsync(string? upTo, CancellationToken cancellationToken = default) => _send.ExecuteAsync(upTo, cancellationToken);

    /// <inheritdoc />
    public Task<GitOperationResult> ResetAsync(string target, CommitResetMode mode, string expectedHead, CancellationToken cancellationToken = default)
        => _reset.ExecuteAsync(target, mode, expectedHead, cancellationToken);

    /// <inheritdoc />
    public async Task<PatchApplyResult> ApplyWaitingAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var metadata = await GetWaitingAsync(patchId, cancellationToken);
            if (_paths.Repository is { IsBound: true } current && !current.IsNamed(metadata.Repository))
            {
                throw new InvalidOperationException(_localizer.Format("Commits.OtherRepository", metadata.Repository ?? "?", current.Name));
            }

            // Once it has started the attempt runs to its end, as the commit of a session does: the branch may already have moved.
            var envelope = metadata.ToEnvelope();
            result = await _applier.ApplyAsync(envelope, retry: true, CancellationToken.None);
            if (result.Status != PatchStatus.Waiting)
            {
                await _storage.DeleteContentAsync(patchId, CancellationToken.None);
            }

            await _outcomes.RecordAsync(metadata, result, CommitHistoryText.Received(envelope, result), CancellationToken.None);
        }

        await PatchOperations.RefreshQuietlyAsync(() => _tracker.MarkReceivedAsync(CancellationToken.None));
        return result;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> DeclineWaitingAsync(Guid patchId, CancellationToken cancellationToken = default)
    {
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var metadata = await GetWaitingAsync(patchId, cancellationToken);
            var result = PatchApplyResult.Rejected(patchId, LocalizedText.Encode("Commits.Declined"));
            await _storage.DeleteContentAsync(patchId, CancellationToken.None);
            await _outcomes.RecordAsync(metadata, result, null, CancellationToken.None);
            return result;
        }
    }

    /// <summary>
    /// Finds a received delivery of commits that waits.
    /// </summary>
    /// <param name="patchId">The delivery.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The delivery.</returns>
    /// <exception cref="InvalidOperationException">The node is not an Origin, or the delivery does not wait.</exception>
    /// <exception cref="KeyNotFoundException">The delivery does not exist.</exception>
    private async Task<PatchMetadata> GetWaitingAsync(Guid patchId, CancellationToken cancellationToken)
    {
        if (_node.Role != NodeRole.Origin)
        {
            throw new InvalidOperationException(_localizer.Format("Commits.OnlyOrigin", _node.Role));
        }

        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new KeyNotFoundException(_localizer.Format("Patch.NotFound", patchId));
        return metadata is { Direction: PatchDirection.Incoming, Status: PatchStatus.Waiting, Commits: not null }
            ? metadata
            : throw new InvalidOperationException(_localizer.Format("Commits.NotWaiting", patchId));
    }
}
