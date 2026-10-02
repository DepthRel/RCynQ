using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Content;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Sync.SyncBase;

namespace RCynQ.Sync.Services;

/// <summary>
/// Builds a patch from the local changes, delivers it to the counterpart endpoint and records the outcome.
/// </summary>
/// <remarks>
/// The working tree lock is held while building the patch and while updating the sync base, but not during the network
/// round trip, so two nodes sending to each other at the same time cannot block each other.
/// </remarks>
public sealed class PatchSendOperation
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The sync paths.</summary>
    private readonly SyncPaths _paths;

    /// <summary>The chain repository.</summary>
    private readonly IChainLinkRepository _chain;

    /// <summary>The patch builder.</summary>
    private readonly IPatchBuilder _builder;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The sender.</summary>
    private readonly IPatchSender _sender;

    /// <summary>The updater of the sync base after a delivery.</summary>
    private readonly SyncBaseUpdater _baseUpdater;

    /// <summary>The store of the diffs shown to the user.</summary>
    private readonly PatchDisplayDiffStore _displayDiffs;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The sync base manager.</summary>
    private readonly SyncBaseManager _syncBase;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchSendOperation"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="builder">The patch builder.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="sender">The sender.</param>
    /// <param name="baseUpdater">The updater of the sync base after a delivery.</param>
    /// <param name="displayDiffs">The store of the diffs shown to the user.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="syncBase">The sync base manager.</param>
    public PatchSendOperation(
        ILocalizer localizer,
        NodeConfig node,
        SyncPaths paths,
        IChainLinkRepository chain,
        IPatchBuilder builder,
        ISignatureService signatures,
        IPatchSender sender,
        SyncBaseUpdater baseUpdater,
        PatchDisplayDiffStore displayDiffs,
        IPathUtility pathUtility,
        IPatchRepository patches,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        WorkingTreeLock workingTreeLock,
        SyncBaseManager syncBase)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _paths = paths;
        _chain = chain;
        _builder = builder;
        _signatures = signatures;
        _sender = sender;
        _baseUpdater = baseUpdater;
        _displayDiffs = displayDiffs;
        _pathUtility = pathUtility;
        _patches = patches;
        _outcomes = outcomes;
        _tracker = tracker;
        _lock = workingTreeLock;
        _syncBase = syncBase;
    }

    /// <summary>
    /// Builds a patch from the changes of the working tree against the sync base and sends it to the counterpart endpoint.
    /// </summary>
    /// <param name="paths">The repository-relative files to send, or <see langword="null"/> to send every changed file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result; <see cref="SendPatchResult.Sent"/> is <see langword="false"/> when there is nothing to send.</returns>
    /// <exception cref="SyncBridgeException">
    /// The node is outside the patch channel, the chain has no route to the counterpart endpoint, or there is no sync base.
    /// </exception>
    public async Task<SendPatchResult> ExecuteAsync(IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default)
    {
        PatchOperations.EnsureParticipant(_node, _localizer);
        var links = await _chain.GetAllAsync(cancellationToken);
        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(links);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
        }

        PatchEnvelope envelope;
        var content = new WorkdirPatchContent(_localizer, _paths.Workdir, _pathUtility);
        using (await _lock.AcquireAsync(cancellationToken))
        {
            var syncBase = await _syncBase.EnsureAsync(cancellationToken);
            if (!syncBase.IsReady)
            {
                throw new SyncBridgeException(syncBase.Problem ?? _localizer.Get("Patch.NoBaseline"));
            }

            envelope = await _builder.BuildAsync(_node.Id, route.Endpoint.Peer.Id, _paths.SyncBase, _paths.Workdir, cancellationToken);

            // What the Synchronization tab lists: the files Git sees as changed, the requested ones (all without a selection).
            // Those the peer already has are sent once more.
            var pending = await _syncBase.SelectPendingAsync(envelope.Manifest, syncBase, cancellationToken);
            var selected = await _syncBase.SelectForSendAsync(pending, syncBase, paths, cancellationToken);
            // The repository is named in what gets signed, so the peer applies the patch to that repository or refuses it.
            envelope = new PatchEnvelope(
                envelope.Id, envelope.SourceNode, envelope.TargetNode, envelope.CreatedAtUtc, envelope.BaseStateHash, envelope.IncomingStateHash, selected,
                repository: _paths.Repository is { IsBound: true } repository ? repository.Name : null);
            if (envelope.Manifest.Count == 0)
            {
                return new SendPatchResult(false, null, null, _localizer.Get("Patch.NothingToSend"));
            }

            envelope = envelope.WithSignature(await _signatures.SignAsync(PayloadCanonicalizer.Build(envelope), cancellationToken));
            await _displayDiffs.SaveAsync(envelope, content, _paths.SyncBase, cancellationToken);
        }

        var metadata = PatchMetadata.FromEnvelope(envelope, PatchDirection.Outgoing);
        await _patches.UpsertAsync(metadata, cancellationToken);

        PatchApplyResult result;
        var delivered = false;
        try
        {
            result = await _sender.SendAsync(envelope, content, links, cancellationToken);
            delivered = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Whatever stopped the delivery, the patch is on record as pending and has to end as failed. Only a
            // cancellation leaves it pending: nobody knows then whether the peer got it.
            result = PatchApplyResult.Failed(envelope.Id, _localizer.Describe(ex));
        }

        // The peer has answered (or the delivery failed). What follows only writes that down on this node, so it is not
        // cut short by a caller that gives up now: a patch the peer applied must not stay "pending" with an old base.
        if (result.Status == PatchStatus.Applied)
        {
            using (await _lock.AcquireAsync(CancellationToken.None))
            {
                // The peer now has the sent files, so they become the base. Files edited while the patch was on its way
                // stay out of the base and travel with the next patch.
                var accepted = await _baseUpdater.UpdateAsync(envelope, _paths.Workdir, _paths.SyncBase, CancellationToken.None);
                await _syncBase.RecordExchangeAsync(accepted.Select(change => change.Path), CancellationToken.None);
            }
        }

        await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Patch.Sent", envelope.Manifest.Count, route.Endpoint.Peer.Id), CancellationToken.None);

        // "Last sent" is the time a patch reached the peer, so a delivery that failed does not move it.
        await PatchOperations.RefreshQuietlyAsync(() => delivered ? _tracker.MarkSentAsync(CancellationToken.None) : _tracker.RefreshAsync(CancellationToken.None));
        return new SendPatchResult(true, envelope.Id, result, Describe(result, envelope.Manifest.Count));
    }

    /// <summary>
    /// Describes a send result for the user.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="fileCount">The number of files in the patch.</param>
    /// <returns>The message.</returns>
    private string Describe(PatchApplyResult result, int fileCount) => result.Status switch
    {
        PatchStatus.Applied => _localizer.Format("Patch.AppliedOnPeer", fileCount),
        PatchStatus.ConflictPending => _localizer.Format("Patch.ConflictOnPeer", result.Conflicts.Count),
        PatchStatus.Pending => result.Error ?? _localizer.Get("Patch.StatusUnknown"),
        _ => _localizer.Format("Patch.NotApplied", result.Error)
    };
}
