using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Core.State;

namespace RCynQ.Sync.Services;

/// <summary>
/// Application facade of the live patch channel used by the service endpoints, CLI and UI.
/// </summary>
public interface IPatchService
{
    /// <summary>
    /// Builds a patch from the sync base to the working tree and sends it to the chain endpoint.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The send result; <see cref="SendPatchResult.Sent"/> is <see langword="false"/> when there are no changes.</returns>
    /// <exception cref="SyncBridgeException">The node cannot send (role, missing chain or baseline).</exception>
    Task<SendPatchResult> SendAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives a patch from the peer.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The processing result.</returns>
    /// <exception cref="PatchRejectedException">The envelope was refused.</exception>
    Task<PatchApplyResult> ReceiveAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an incoming patch that is waiting for a conflict decision or failed.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="policy">The user's policy.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The resolution result.</returns>
    /// <exception cref="KeyNotFoundException">The patch does not exist.</exception>
    /// <exception cref="InvalidOperationException">The patch cannot be resolved in its current state.</exception>
    Task<PatchApplyResult> ResolveAsync(Guid patchId, ConflictPolicy policy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the synchronization status of the local node.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status.</returns>
    Task<NodeSyncStatus> GetStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent patches, newest first.
    /// </summary>
    /// <param name="count">The maximum number of patches.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The patches.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The patch, or <see langword="null"/>.</returns>
    Task<PatchMetadata?> GetAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the unified diff of a stored patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The diff bytes.</returns>
    /// <exception cref="SyncBridgeException">The payload is not stored.</exception>
    Task<byte[]> ReadDiffAsync(Guid patchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the sync base with the current working tree.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The recorded snapshot.</returns>
    Task<SnapshotInfo> InitializeBaselineAsync(CancellationToken cancellationToken = default);
}
