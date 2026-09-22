using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches.Diff;
using RCynQ.Localization;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Default <see cref="IPatchReceiver"/>.
/// </summary>
/// <remarks>
/// <para>
/// A patch arrives as a session. <see cref="OpenSessionAsync"/> validates everything before anything is stored: role, link token
/// (constant time), sender header, receiver, known counterpart peer with signing key, signature of the manifest, and replay protection
/// (under the working tree lock, so concurrent duplicates cannot both pass). The manifest is stored as an incoming <see cref="PatchStatus.Pending"/> patch.
/// A repeated opening of the same session with the same signature resumes it.
/// </para>
/// <para>
/// <see cref="ReceiveContentAsync"/> stores the content of one file only after it matches the size and SHA-256 hash the signed manifest
/// announces. <see cref="CommitAsync"/> then makes the apply attempt. It is all-or-nothing: when any file conflicts with a local change
/// nothing is written and the patch becomes <see cref="PatchStatus.ConflictPending"/>; otherwise the content is written to the working tree and the sync base.
/// </para>
/// </remarks>
public sealed class DefaultPatchReceiver : IPatchReceiver
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The replay guard.</summary>
    private readonly ReplayGuard _replay;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The diff builder.</summary>
    private readonly IPatchDiffBuilder _diffs;

    /// <summary>The conflict detector.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The content applier.</summary>
    private readonly ContentOverwriteApplier _applier;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultPatchReceiver"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="replay">The replay guard.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="diffs">The diff builder.</param>
    /// <param name="detector">The conflict detector.</param>
    /// <param name="applier">The content applier.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="timeProvider">The clock.</param>
    public DefaultPatchReceiver(
        NodeConfig node,
        IPeerRepository peers,
        ISignatureService signatures,
        ReplayGuard replay,
        LinkTokenProvider tokens,
        IPatchRepository patches,
        IPatchFileStorage storage,
        IPatchDiffBuilder diffs,
        ConflictDetector detector,
        ContentOverwriteApplier applier,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        SyncPaths paths,
        WorkingTreeLock workingTreeLock,
        TimeProvider timeProvider)
    {
        _node = node;
        _peers = peers;
        _signatures = signatures;
        _replay = replay;
        _tokens = tokens;
        _patches = patches;
        _storage = storage;
        _diffs = diffs;
        _detector = detector;
        _applier = applier;
        _outcomes = outcomes;
        _tracker = tracker;
        _paths = paths;
        _lock = workingTreeLock;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        await ValidateAsync(envelope, context, cancellationToken);

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
                throw new PatchRejectedException(PatchRejectionReason.Replay, Texts.Format("Replay.Again", envelope.Id));
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
        var session = await AuthorizeSessionAsync(patchId, context, cancellationToken);
        var hash = sha256.Trim().ToLowerInvariant();
        var entry = session.Manifest.FirstOrDefault(change => change.Kind != ChangeKind.Deleted && string.Equals(change.Sha256, hash, StringComparison.Ordinal))
            ?? throw new PatchRejectedException(PatchRejectionReason.Malformed, Texts.Format("Receive.UnexpectedContent", hash, patchId));

        try
        {
            await _storage.SaveUploadAsync(hash, content, entry.SizeBytes ?? 0, cancellationToken);
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException)
        {
            throw new PatchRejectedException(PatchRejectionReason.Malformed, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await AuthorizeSessionAsync(patchId, context, cancellationToken);

        using (await _lock.AcquireAsync(cancellationToken))
        {
            // Read again under the lock: a duplicate commit that waited for the first one sees the finished patch.
            var metadata = await _patches.GetAsync(patchId, cancellationToken);
            if (metadata is null || metadata.Direction != PatchDirection.Incoming || metadata.Status != PatchStatus.Pending)
            {
                throw new PatchRejectedException(PatchRejectionReason.Replay, Texts.Format("Replay.Again", patchId));
            }

            var envelope = ToEnvelope(metadata);
            var needed = envelope.ContentHashes;
            var missing = 0;
            foreach (var hash in needed)
            {
                if (!await _storage.HasContentAsync(patchId, hash, cancellationToken))
                {
                    missing++;
                }
            }

            if (missing > 0)
            {
                throw new PatchRejectedException(PatchRejectionReason.Malformed, Texts.Format("Receive.ContentIncomplete", missing, patchId));
            }

            PatchApplyResult result;
            try
            {
                await _storage.StageContentAsync(patchId, needed.ToArray(), cancellationToken);
                var content = new StoredPatchContent(patchId, _storage);
                await SaveDisplayDiffAsync(envelope, content, cancellationToken);
                if (!_paths.BaselineInitialized)
                {
                    result = PatchApplyResult.Failed(envelope.Id, LocalizedText.Encode("Receive.NoBaseline"));
                }
                else
                {
                    var conflicts = await _detector.DetectAsync(envelope.Manifest, _paths.Workdir, _paths.SyncBase, cancellationToken);
                    if (conflicts.Count > 0)
                    {
                        result = PatchApplyResult.Conflict(envelope.Id, conflicts);
                    }
                    else
                    {
                        result = await _applier.ApplyAsync(envelope, content, _paths.Workdir, cancellationToken);
                        await _applier.ApplyAsync(envelope, content, _paths.SyncBase, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is PatchApplyException or SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(envelope.Id, ex.Message);
            }

            // Content of a patch that is done is dropped; a patch that waits for a decision keeps it for the resolution.
            if (result.Status == PatchStatus.Applied)
            {
                await _storage.DeleteContentAsync(patchId, cancellationToken);
            }

            await _outcomes.RecordAsync(metadata, result, null, cancellationToken);
            await _tracker.MarkReceivedAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>
    /// Validates a manifest before anything is stored.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is valid.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    private async Task ValidateAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync(context, cancellationToken);
        if (!NodeId.TryParse(context.SenderNodeId, out var headerSender) || headerSender != envelope.SourceNode)
        {
            throw new PatchRejectedException(PatchRejectionReason.SenderMismatch, Texts.Get("Receive.SenderMismatch"));
        }

        if (envelope.TargetNode != _node.Id)
        {
            throw new PatchRejectedException(PatchRejectionReason.WrongReceiver, Texts.Format("Receive.WrongReceiver", envelope.TargetNode, _node.Id));
        }

        var expectedRole = _node.Role == NodeRole.Source ? NodeRole.Dest : NodeRole.Source;
        var peer = await _peers.GetAsync(envelope.SourceNode, cancellationToken);
        if (peer?.SigningPublicKey is null || peer.Role != expectedRole)
        {
            throw new PatchRejectedException(PatchRejectionReason.UnknownSender, $"{envelope.SourceNode} is not a known {expectedRole} peer with a signing key.");
        }

        if (!envelope.IsSigned
            || !_signatures.Verify(PayloadCanonicalizer.Build(envelope), envelope.Signature, Convert.FromBase64String(peer.SigningPublicKey)))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidSignature, Texts.Get("Receive.InvalidSignature"));
        }
    }

    /// <summary>
    /// Checks the role and the link token of a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is allowed.</returns>
    /// <exception cref="PatchRejectedException">The node does not accept patches or the token is wrong.</exception>
    private async Task EnsureAuthorizedAsync(PatchReceiveContext context, CancellationToken cancellationToken)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, Texts.Format("Receive.RoleNotAllowed", _node.Role));
        }

        if (!LinkTokenProvider.Matches(await _tokens.GetTokenAsync(cancellationToken), context.LinkToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidToken, Texts.Get("Receive.InvalidToken"));
        }
    }

    /// <summary>
    /// Checks a request that belongs to an open session: the token, the sender and the session itself.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored patch of the session.</returns>
    /// <exception cref="PatchRejectedException">The request is not allowed or the session is unknown.</exception>
    private async Task<PatchMetadata> AuthorizeSessionAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken)
    {
        await EnsureAuthorizedAsync(context, cancellationToken);
        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new PatchRejectedException(PatchRejectionReason.Malformed, Texts.Format("Receive.UnknownSession", patchId));
        if (metadata.Direction != PatchDirection.Incoming)
        {
            throw new PatchRejectedException(PatchRejectionReason.Malformed, Texts.Format("Receive.UnknownSession", patchId));
        }

        if (!NodeId.TryParse(context.SenderNodeId, out var sender) || sender != metadata.SourceNode)
        {
            throw new PatchRejectedException(PatchRejectionReason.SenderMismatch, Texts.Get("Receive.SenderMismatch"));
        }

        if (metadata.Status != PatchStatus.Pending)
        {
            throw new PatchRejectedException(PatchRejectionReason.Replay, Texts.Format("Replay.Again", patchId));
        }

        return metadata;
    }

    /// <summary>
    /// Rebuilds the envelope of a stored patch.
    /// </summary>
    /// <param name="metadata">The stored patch.</param>
    /// <returns>The envelope.</returns>
    private static PatchEnvelope ToEnvelope(PatchMetadata metadata)
        => new(metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash, metadata.Manifest, metadata.Signature);

    /// <summary>
    /// Builds and stores the diff shown to the user, before the patch changes the sync base it is built from.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <param name="content">The staged content.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the diff is stored, or skipped because Git is not available.</returns>
    private async Task SaveDisplayDiffAsync(PatchEnvelope envelope, IPatchContent content, CancellationToken cancellationToken)
    {
        if (!_paths.BaselineInitialized)
        {
            return;
        }

        try
        {
            var diff = await _diffs.BuildAsync(envelope.Manifest, _paths.SyncBase, content, PatchDiffLimits.Display, cancellationToken);
            await _storage.SaveDiffAsync(envelope.Id, diff, cancellationToken);
        }
        catch (GitExecutionException)
        {
            // The diff is a convenience for the user; a missing Git must not stop the patch.
        }
    }
}
