using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Paths;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Git.Patches.Diff;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Baseline;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.Content;
using RCynQ.Sync.GitChannel;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;
using RCynQ.Localization;

namespace RCynQ.Sync.Services;

/// <summary>
/// Default <see cref="IPatchService"/>.
/// </summary>
/// <remarks>
/// The working tree lock is held while building a patch and while updating the sync base, but not during the network
/// round trip, so two nodes sending to each other at the same time cannot block each other.
/// </remarks>
public sealed class PatchService : IPatchService
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

    /// <summary>The receiver.</summary>
    private readonly IPatchReceiver _receiver;

    /// <summary>The conflict resolver.</summary>
    private readonly IConflictResolver _resolver;

    /// <summary>The conflict detector.</summary>
    private readonly ConflictDetector _detector;

    /// <summary>The updater of the sync base after a delivery.</summary>
    private readonly SyncBaseUpdater _baseUpdater;

    /// <summary>The diff builder.</summary>
    private readonly IPatchDiffBuilder _diffs;

    /// <summary>The path utility.</summary>
    private readonly IPathUtility _pathUtility;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>The payload storage.</summary>
    private readonly IPatchFileStorage _storage;

    /// <summary>The outcome recorder.</summary>
    private readonly PatchOutcomeRecorder _outcomes;

    /// <summary>The state tracker.</summary>
    private readonly StateTracker _tracker;

    /// <summary>The baseline service.</summary>
    private readonly BaselineService _baseline;

    /// <summary>The working tree lock.</summary>
    private readonly WorkingTreeLock _lock;

    /// <summary>The resolver of the directory to compare the working tree against.</summary>
    private readonly IPendingChangesBaseResolver _sendBaseResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchService"/> class.
    /// </summary>
    /// <param name="node">The local node.</param>
    /// <param name="paths">The sync paths.</param>
    /// <param name="chain">The chain repository.</param>
    /// <param name="builder">The patch builder.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="sender">The sender.</param>
    /// <param name="receiver">The receiver.</param>
    /// <param name="resolver">The conflict resolver.</param>
    /// <param name="detector">The conflict detector.</param>
    /// <param name="baseUpdater">The updater of the sync base after a delivery.</param>
    /// <param name="diffs">The diff builder.</param>
    /// <param name="pathUtility">The path utility.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="baseline">The baseline service.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
    /// <param name="sendBaseResolver">The resolver of the directory to compare the working tree against.</param>
    public PatchService(
        NodeConfig node,
        SyncPaths paths,
        IChainLinkRepository chain,
        IPatchBuilder builder,
        ISignatureService signatures,
        IPatchSender sender,
        IPatchReceiver receiver,
        IConflictResolver resolver,
        ConflictDetector detector,
        SyncBaseUpdater baseUpdater,
        IPatchDiffBuilder diffs,
        IPathUtility pathUtility,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        BaselineService baseline,
        WorkingTreeLock workingTreeLock,
        IPendingChangesBaseResolver sendBaseResolver)
    {
        _node = node;
        _paths = paths;
        _chain = chain;
        _builder = builder;
        _signatures = signatures;
        _sender = sender;
        _receiver = receiver;
        _resolver = resolver;
        _detector = detector;
        _baseUpdater = baseUpdater;
        _diffs = diffs;
        _sendBaseResolver = sendBaseResolver;
        _pathUtility = pathUtility;
        _patches = patches;
        _storage = storage;
        _outcomes = outcomes;
        _tracker = tracker;
        _baseline = baseline;
        _lock = workingTreeLock;
    }

    /// <inheritdoc />
    public async Task<SendPatchResult> SendAsync(CancellationToken cancellationToken = default)
    {
        EnsureParticipant();
        var links = await _chain.GetAllAsync(cancellationToken);
        ChainRoute route;
        try
        {
            route = ChainRoute.Resolve(links);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(ex.Message, ex);
        }

        PatchEnvelope envelope;
        WorkdirPatchContent content;
        bool isGitNativeBase;
        await using (var sendBase = await _sendBaseResolver.ResolveAsync(cancellationToken)
            ?? throw new SyncBridgeException(Texts.Get("Patch.NoBaseline")))
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                envelope = await _builder.BuildAsync(_node.Id, route.Endpoint.Peer.Id, sendBase.Directory, _paths.Workdir, cancellationToken);
            }

            if (envelope.Manifest.Count == 0)
            {
                return new SendPatchResult(false, null, null, Texts.Get("Patch.NothingToSend"));
            }

            envelope = envelope.WithSignature(await _signatures.SignAsync(PayloadCanonicalizer.Build(envelope), cancellationToken));
            content = new WorkdirPatchContent(_paths.Workdir, _pathUtility);
            await SaveDisplayDiffAsync(envelope, content, sendBase.Directory, cancellationToken);
            isGitNativeBase = sendBase.IsGitNative;
        }

        var metadata = PatchMetadata.FromEnvelope(envelope, PatchDirection.Outgoing);
        await _patches.UpsertAsync(metadata, cancellationToken);

        PatchApplyResult result;
        try
        {
            result = await _sender.SendAsync(envelope, content, links, cancellationToken);
        }
        catch (Exception ex) when (ex is SyncBridgeException or IOException or UnauthorizedAccessException)
        {
            result = PatchApplyResult.Failed(envelope.Id, ex.Message);
        }

        if (result.Status == PatchStatus.Applied && !isGitNativeBase)
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                // Files edited while the patch was on its way stay out of the base and travel with the next patch.
                // Not needed in Git-native mode: the comparison base is always freshly exported from what Dest last had.
                await _baseUpdater.UpdateAsync(envelope, _paths.Workdir, _paths.SyncBase, cancellationToken);
            }
        }

        await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Patch.Sent", envelope.Manifest.Count, route.Endpoint.Peer.Id), cancellationToken);
        await _tracker.MarkSentAsync(cancellationToken);
        return new SendPatchResult(true, envelope.Id, result, Describe(result, envelope.Manifest.Count));
    }

    /// <inheritdoc />
    public Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.OpenSessionAsync(envelope, context, cancellationToken);

    /// <inheritdoc />
    public Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.ReceiveContentAsync(patchId, sha256, content, context, cancellationToken);

    /// <inheritdoc />
    public Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.CommitAsync(patchId, context, cancellationToken);

    /// <inheritdoc />
    public async Task<PatchApplyResult> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        EnsureParticipant();
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentException($"Conflict policy '{policy}' is not defined.", nameof(policy));
        }

        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new KeyNotFoundException(Texts.Format("Patch.NotFound", patchId));
        if (metadata.Direction != PatchDirection.Incoming || metadata.Status is not (PatchStatus.ConflictPending or PatchStatus.Failed))
        {
            throw new InvalidOperationException(Texts.Format("Patch.NotResolvable", patchId, metadata.Direction, metadata.Status));
        }

        var envelope = new PatchEnvelope(
            metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash,
            metadata.Manifest, metadata.Signature);

        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            try
            {
                var conflicts = await _detector.DetectAsync(envelope.Manifest, _paths.Workdir, _paths.SyncBase, cancellationToken);
                result = await _resolver.ResolveAsync(envelope, new StoredPatchContent(patchId, _storage), conflicts, policy, cancellationToken);
            }
            catch (Exception ex) when (ex is PatchApplyException or SyncBridgeException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(patchId, ex.Message);
            }

            // A patch that is decided no longer needs its content; one that still waits for a decision keeps it.
            if (result.Status is PatchStatus.ConflictResolved or PatchStatus.Rejected)
            {
                await _storage.DeleteContentAsync(patchId, cancellationToken);
            }
        }

        await _outcomes.RecordAsync(metadata, result, LocalizedText.Encode("Patch.ResolvedWith", policy), cancellationToken);
        await _tracker.RefreshAsync(cancellationToken);
        return result;
    }

    /// <inheritdoc />
    public async Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default)
    {
        var state = await _tracker.RefreshAsync(cancellationToken);
        var pending = await _tracker.GetPendingChangesAsync(cancellationToken);
        var recent = await _patches.GetRecentAsync(500, cancellationToken);
        var conflicts = recent.Count(patch => patch.Direction == PatchDirection.Incoming && patch.Status == PatchStatus.ConflictPending);
        var baseline = await _tracker.IsSendReadyAsync(cancellationToken);
        return new NodeSyncStatus(_node.Id, _node.Name, _node.Role, state, baseline, pending, conflicts);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default)
        => _patches.GetRecentAsync(count, cancellationToken);

    /// <inheritdoc />
    public Task<PatchMetadata?> GetAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _patches.GetAsync(patchId, cancellationToken);

    /// <inheritdoc />
    public Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default)
        => _storage.ReadDiffAsync(patchId, cancellationToken);

    /// <inheritdoc />
    public Task<SnapshotInfo> InitializeBaselineAsync(CancellationToken cancellationToken = default)
        => _baseline.CreateBaselineAsync(cancellationToken);

    /// <summary>
    /// Builds and stores the diff shown to the user, before the patch changes the comparison base it is built from.
    /// </summary>
    /// <param name="envelope">The patch.</param>
    /// <param name="content">The source of the incoming content.</param>
    /// <param name="baseDirectory">The directory the patch was built against.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the diff is stored, or skipped because Git is not available.</returns>
    private async Task SaveDisplayDiffAsync(PatchEnvelope envelope, IPatchContent content, string baseDirectory, CancellationToken cancellationToken)
    {
        try
        {
            var diff = await _diffs.BuildAsync(envelope.Manifest, baseDirectory, content, PatchDiffLimits.Display, cancellationToken);
            await _storage.SaveDiffAsync(envelope.Id, diff, cancellationToken);
        }
        catch (GitExecutionException)
        {
            // The diff is a convenience for the user; a missing Git must not stop the patch.
        }
    }

    /// <summary>
    /// Throws for nodes outside the patch channel.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is a Mediator.</exception>
    private void EnsureParticipant()
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new SyncBridgeException(Texts.Format("Patch.NoChannel", _node.Role));
        }
    }

    /// <summary>
    /// Describes a send result for the user.
    /// </summary>
    /// <param name="result">The result.</param>
    /// <param name="fileCount">The number of files in the patch.</param>
    /// <returns>The message.</returns>
    private static string Describe(PatchApplyResult result, int fileCount) => result.Status switch
    {
        PatchStatus.Applied => Texts.Format("Patch.AppliedOnPeer", fileCount),
        PatchStatus.ConflictPending => Texts.Format("Patch.ConflictOnPeer", result.Conflicts.Count),
        PatchStatus.Pending => result.Error ?? Texts.Get("Patch.StatusUnknown"),
        _ => Texts.Format("Patch.NotApplied", result.Error)
    };
}
