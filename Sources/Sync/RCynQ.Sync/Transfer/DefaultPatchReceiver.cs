using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Git.Patches;
using RCynQ.Git.Patches.Bundles;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Conflicts;
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
/// Validation happens before anything is stored: role, link token (constant time), sender header, receiver, known
/// counterpart peer with signing key, signature, bundle integrity. Replay protection and storage run under the
/// working tree lock so concurrent duplicates cannot both pass.
/// </para>
/// <para>
/// The initial apply is all-or-nothing: when any file conflicts with a local change nothing is written and the patch
/// becomes <see cref="PatchStatus.ConflictPending"/>; otherwise the bundle is written to the working tree and the sync base.
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

    /// <summary>The bundle builder.</summary>
    private readonly IFileBundleBuilder _bundles;

    /// <summary>The conflict detector.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The bundle applier.</summary>
    private readonly BundleOverwriteApplier _applier;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

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
    /// <param name="bundles">The bundle builder.</param>
    /// <param name="detector">The conflict detector.</param>
    /// <param name="applier">The bundle applier.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    public DefaultPatchReceiver(
        NodeConfig node,
        IPeerRepository peers,
        ISignatureService signatures,
        ReplayGuard replay,
        LinkTokenProvider tokens,
        IPatchRepository patches,
        IPatchFileStorage storage,
        IFileBundleBuilder bundles,
        ConflictDetector detector,
        BundleOverwriteApplier applier,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        SyncPaths paths,
        WorkingTreeLock workingTreeLock)
    {
        _node = node;
        _peers = peers;
        _signatures = signatures;
        _replay = replay;
        _tokens = tokens;
        _patches = patches;
        _storage = storage;
        _bundles = bundles;
        _detector = detector;
        _applier = applier;
        _outcomes = outcomes;
        _tracker = tracker;
        _paths = paths;
        _lock = workingTreeLock;
    }

    /// <inheritdoc />
    public async Task<PatchApplyResult> ReceiveAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        await ValidateAsync(envelope, context, cancellationToken);

        using (await _lock.AcquireAsync(cancellationToken))
        {
            await _replay.EnsureFreshAsync(envelope, cancellationToken);
            await _storage.SaveAsync(envelope.Id, envelope.Diff, envelope.Bundle, cancellationToken);
            var metadata = PatchMetadata.FromEnvelope(envelope, PatchDirection.Incoming);
            await _patches.UpsertAsync(metadata, cancellationToken);

            PatchApplyResult result;
            try
            {
                if (!_paths.BaselineInitialized)
                {
                    result = PatchApplyResult.Failed(envelope.Id, "The sync base is not initialized on the receiving node; run 'patch baseline' first.");
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
                        result = await _applier.ApplyAsync(envelope, _paths.Workdir, cancellationToken);
                        await _applier.ApplyAsync(envelope, _paths.SyncBase, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is PatchApplyException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(envelope.Id, ex.Message);
            }

            await _outcomes.RecordAsync(metadata, result, null, cancellationToken);
            await _tracker.MarkReceivedAsync(cancellationToken);
            return result;
        }
    }

    /// <summary>
    /// Validates the request before anything is stored.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is valid.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    private async Task ValidateAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, $"A {_node.Role} node does not accept patches.");
        }

        if (!LinkTokenProvider.Matches(await _tokens.GetTokenAsync(cancellationToken), context.LinkToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidToken, "The link token is missing or invalid.");
        }

        if (!NodeId.TryParse(context.SenderNodeId, out var headerSender) || headerSender != envelope.SourceNode)
        {
            throw new PatchRejectedException(PatchRejectionReason.SenderMismatch, "The X-Node-Id header does not match the envelope sender.");
        }

        if (envelope.TargetNode != _node.Id)
        {
            throw new PatchRejectedException(PatchRejectionReason.WrongReceiver, $"The patch is addressed to {envelope.TargetNode}, not {_node.Id}.");
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
            throw new PatchRejectedException(PatchRejectionReason.InvalidSignature, "The patch signature is missing or invalid.");
        }

        try
        {
            _bundles.Read(envelope.Bundle, envelope.Manifest);
        }
        catch (FormatException ex)
        {
            throw new PatchRejectedException(PatchRejectionReason.Malformed, ex.Message);
        }
    }
}
