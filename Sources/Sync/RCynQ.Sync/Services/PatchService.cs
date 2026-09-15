using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Abstractions.Storage;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;
using RCynQ.Ssh.Commands;
using RCynQ.Ssh.Routing;
using RCynQ.Sync.Apply;
using RCynQ.Sync.Baseline;
using RCynQ.Sync.Conflicts;
using RCynQ.Sync.History;
using RCynQ.Sync.Locking;
using RCynQ.Sync.Paths;
using RCynQ.Sync.Signing;
using RCynQ.Sync.State;

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

    /// <summary>The bundle applier.</summary>
    private readonly BundleOverwriteApplier _applier;

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
    /// <param name="applier">The bundle applier.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="storage">The payload storage.</param>
    /// <param name="outcomes">The outcome recorder.</param>
    /// <param name="tracker">The state tracker.</param>
    /// <param name="baseline">The baseline service.</param>
    /// <param name="workingTreeLock">The working tree lock.</param>
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
        BundleOverwriteApplier applier,
        IPatchRepository patches,
        IPatchFileStorage storage,
        PatchOutcomeRecorder outcomes,
        StateTracker tracker,
        BaselineService baseline,
        WorkingTreeLock workingTreeLock)
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
        _applier = applier;
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

        if (!_paths.BaselineInitialized)
        {
            throw new SyncBridgeException("The sync base is not initialized. Run 'patch baseline' on both nodes while their working trees are identical.");
        }

        PatchEnvelope envelope;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            envelope = await _builder.BuildAsync(_node.Id, route.Endpoint.Peer.Id, _paths.SyncBase, _paths.Workdir, cancellationToken);
        }

        if (envelope.Manifest.Count == 0)
        {
            return new SendPatchResult(false, null, null, "No changes to send.");
        }

        envelope = envelope.WithSignature(await _signatures.SignAsync(PayloadCanonicalizer.Build(envelope), cancellationToken));
        await _storage.SaveAsync(envelope.Id, envelope.Diff, envelope.Bundle, cancellationToken);
        var metadata = PatchMetadata.FromEnvelope(envelope, PatchDirection.Outgoing);
        await _patches.UpsertAsync(metadata, cancellationToken);

        PatchApplyResult result;
        try
        {
            result = await _sender.SendAsync(envelope, links, cancellationToken);
        }
        catch (SyncBridgeException ex)
        {
            result = PatchApplyResult.Failed(envelope.Id, ex.Message);
        }

        if (result.Status == PatchStatus.Applied)
        {
            using (await _lock.AcquireAsync(cancellationToken))
            {
                await _applier.ApplyAsync(envelope, _paths.SyncBase, cancellationToken);
            }
        }

        await _outcomes.RecordAsync(metadata, result, $"Sent {envelope.Manifest.Count} file(s) to {route.Endpoint.Peer.Id}.", cancellationToken);
        await _tracker.MarkSentAsync(cancellationToken);
        return new SendPatchResult(true, envelope.Id, result, Describe(result, envelope.Manifest.Count));
    }

    /// <inheritdoc />
    public Task<PatchApplyResult> ReceiveAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
        => _receiver.ReceiveAsync(envelope, context, cancellationToken);

    /// <inheritdoc />
    public async Task<PatchApplyResult> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default)
    {
        EnsureParticipant();
        if (!Enum.IsDefined(policy))
        {
            throw new ArgumentException($"Conflict policy '{policy}' is not defined.", nameof(policy));
        }

        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new KeyNotFoundException($"Patch {patchId} was not found.");
        if (metadata.Direction != PatchDirection.Incoming || metadata.Status is not (PatchStatus.ConflictPending or PatchStatus.Failed))
        {
            throw new InvalidOperationException($"Patch {patchId} is an {metadata.Direction} patch in state {metadata.Status}; only incoming patches that are ConflictPending or Failed can be resolved.");
        }

        var envelope = new PatchEnvelope(
            metadata.Id, metadata.SourceNode, metadata.TargetNode, metadata.CreatedAtUtc, metadata.BaseStateHash, metadata.IncomingStateHash,
            metadata.Manifest,
            await _storage.ReadDiffAsync(patchId, cancellationToken),
            await _storage.ReadBundleAsync(patchId, cancellationToken),
            metadata.Signature);

        PatchApplyResult result;
        using (await _lock.AcquireAsync(cancellationToken))
        {
            try
            {
                var conflicts = await _detector.DetectAsync(envelope.Manifest, _paths.Workdir, _paths.SyncBase, cancellationToken);
                result = await _resolver.ResolveAsync(envelope, conflicts, policy, cancellationToken);
            }
            catch (Exception ex) when (ex is PatchApplyException or IOException or UnauthorizedAccessException)
            {
                result = PatchApplyResult.Failed(patchId, ex.Message);
            }
        }

        await _outcomes.RecordAsync(metadata, result, $"Resolved with {policy}.", cancellationToken);
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
        var baseline = _node.ParticipatesInPatchChannel && _paths.BaselineInitialized;
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
    /// Throws for nodes outside the patch channel.
    /// </summary>
    /// <exception cref="SyncBridgeException">The node is a Mediator.</exception>
    private void EnsureParticipant()
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new SyncBridgeException($"A {_node.Role} node does not take part in the patch channel.");
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
        PatchStatus.Applied => $"Patch with {fileCount} file(s) applied on the peer.",
        PatchStatus.ConflictPending => $"Patch delivered; {result.Conflicts.Count} file(s) conflict on the peer and wait for a decision there.",
        PatchStatus.Pending => result.Error ?? "Patch delivered; status unknown.",
        _ => $"Patch was not applied: {result.Error}"
    };
}
