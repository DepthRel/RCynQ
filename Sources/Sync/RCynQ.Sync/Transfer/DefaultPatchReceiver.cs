using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Security;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Default <see cref="IPatchReceiver"/>.
/// </summary>
/// <remarks>
/// <para>
/// A patch arrives as a session. <see cref="OpenSessionAsync"/> validates everything before anything is stored: role, link token
/// (constant time), sender header, receiver, known counterpart peer with signing key and signature of the manifest
/// (<see cref="PatchAdmission"/>), and replay protection (under the working tree lock, so concurrent duplicates cannot both pass). The manifest is stored as an incoming <see cref="PatchStatus.Pending"/> patch.
/// A repeated opening of the same session with the same signature resumes it.
/// </para>
/// <para>
/// <see cref="ReceiveContentAsync"/> stores the content of one file only after it matches the size and SHA-256 hash the signed manifest
/// announces. <see cref="CommitAsync"/> then makes the apply attempt (<see cref="IncomingPatchApplier"/>). It is all-or-nothing: when any file conflicts with a local change
/// nothing is written and the patch becomes <see cref="PatchStatus.ConflictPending"/>; otherwise the content is written to the working tree and the sync base.
/// </para>
/// </remarks>
public sealed class DefaultPatchReceiver : IPatchReceiver
{
    /// <summary>Decides whether a request is let in.</summary>
    private readonly PatchAdmission _admission;

    /// <summary>Makes the apply attempt of a patch whose content has arrived.</summary>
    private readonly IncomingPatchApplier _incoming;

    /// <summary>The replay guard.</summary>
    private readonly ReplayGuard _replay;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPatchReceiver"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="admission">Decides whether a request is let in.</param>
    /// <param name="incoming">Makes the apply attempt of a patch whose content has arrived.</param>
    /// <param name="replay">The replay guard.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="timeProvider">The clock.</param>
    public DefaultPatchReceiver(
        ILocalizer localizer,
        PatchAdmission admission,
        IncomingPatchApplier incoming,
        ReplayGuard replay,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _admission = admission;
        _incoming = incoming;
        _replay = replay;
        _patches = patches;
        _storage = storage;
        _outcomes = outcomes;
        _tracker = tracker;
        _lock = workingTreeLock;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        await _admission.ValidateAsync(envelope, context, cancellationToken);

        using (await _lock.AcquireAsync(cancellationToken))
        {
            var existing = await _patches.GetAsync(envelope.Id, cancellationToken);
            if (existing is null)
            {
                await _replay.EnsureFreshAsync(envelope, cancellationToken);
                await _patches.UpsertAsync(PatchMetadata.FromEnvelope(envelope, PatchDirection.Incoming), cancellationToken);
            }
            else if (existing.Direction == PatchDirection.Incoming && existing.Status == PatchStatus.Pending && existing.Signature.AsSpan().SequenceEqual(envelope.Signature))
            {
                _replay.EnsureInWindow(envelope);
            }
            else
            {
                throw new PatchRejectedException(PatchRejectionReason.Replay, _localizer.Format("Replay.Again", envelope.Id));
            }
        }

        // Uploads nobody committed for longer than a patch may live can never be used any more.
        await _storage.PruneUploadsAsync(_timeProvider.GetUtcNow() - _replay.MaxAge, cancellationToken);
        var missing = new List<string>();
        foreach (var hash in envelope.ContentHashes)
        {
            if (!await _storage.HasContentAsync(envelope.Id, hash, cancellationToken))
            {
                missing.Add(hash);
            }
        }

        return new PatchSession(envelope.Id, missing);
    }

    /// <inheritdoc />
    public async Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sha256);
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(context);
        var session = await _admission.AuthorizeSessionAsync(patchId, context, cancellationToken);
        var hash = sha256.Trim().ToLowerInvariant();
        var entry = session.Manifest.FirstOrDefault(change => change.Kind != ChangeKind.Deleted && string.Equals(change.Sha256, hash, StringComparison.Ordinal))
            ?? throw new PatchRejectedException(PatchRejectionReason.Malformed, _localizer.Format("Receive.UnexpectedContent", hash, patchId));

        try
        {
            await _storage.SaveUploadAsync(hash, content, entry.SizeBytes ?? 0, cancellationToken);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new PatchRejectedException(PatchRejectionReason.Malformed, _localizer.Describe(ex));
        }
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await _admission.AuthorizeSessionAsync(patchId, context, cancellationToken);

        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            // Read again under the lock: a duplicate commit that waited for the first one sees the finished patch.
            var metadata = await _patches.GetAsync(patchId, cancellationToken);
            if (metadata is null || metadata.Direction != PatchDirection.Incoming || metadata.Status != PatchStatus.Pending)
            {
                throw new PatchRejectedException(PatchRejectionReason.Replay, _localizer.Format("Replay.Again", patchId));
            }

            var envelope = ToEnvelope(metadata);
            var missing = 0;
            foreach (var hash in envelope.ContentHashes)
            {
                if (!await _storage.HasContentAsync(patchId, hash, cancellationToken))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                throw new PatchRejectedException(PatchRejectionReason.Malformed, _localizer.Format("Receive.ContentIncomplete", missing, patchId));
            }

            // From here the patch is decided: its content is staged, the working tree and the base are written and the
            // outcome is recorded. A sender that gives up meanwhile (its request times out, the tunnel drops) aborts the
            // request, and stopping here on that signal would leave a half-written tree and a patch that stays "pending"
            // although files of it are already in place. So the commit runs to its end once it has started.
            var uninterrupted = CancellationToken.None;
            result = await _incoming.ApplyAsync(envelope, uninterrupted);

            // Content of a patch that is done is dropped; a patch that waits for a decision keeps it for the resolution.
            if (result.Status == PatchStatus.Applied)
            {
                await _storage.DeleteContentAsync(patchId, uninterrupted);
            }

            await _outcomes.RecordAsync(metadata, result, null, uninterrupted);
        }

        // Outside the lock: the tracker takes it itself to read the sync base. The patch is already decided and recorded, so a
        // failing refresh must not turn the answer to the sender into an error.
        try
        {
            await _tracker.MarkReceivedAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            // The next status request refreshes the state again.
        }

        return result;
    }

    /// <summary>
    /// Rebuilds the envelope of a stored patch.
    /// </summary>
    /// <param name="metadata">The stored patch.</param>
    /// <returns>The envelope.</returns>
    private static PatchEnvelope ToEnvelope(PatchMetadata metadata)
        => new(metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash, metadata.Manifest, metadata.Signature);
}
