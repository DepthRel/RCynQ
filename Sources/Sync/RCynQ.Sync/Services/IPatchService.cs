using RCynQ.Core.Api;
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
    /// Builds a patch from the changed files of the working tree and sends it to the chain endpoint.
    /// </summary>
    /// <param name="paths">
    /// The relative paths of the files to send, or <see langword="null"/> to send every changed file, also those the peer already
    /// has. Paths that are not changed files are ignored.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The send result; <see cref="SendPatchResult.Sent"/> is <see langword="false"/> when there is nothing to send.</returns>
    /// <exception cref="SyncBridgeException">The node cannot send (role, missing chain, or no sync base yet).</exception>
    Task<SendPatchResult> SendAsync(IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens (or resumes) the session of a patch from the peer with its signed manifest.
    /// </summary>
    /// <param name="envelope">The envelope with the manifest.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session with the content that is still missing.</returns>
    /// <exception cref="PatchRejectedException">The envelope was refused.</exception>
    Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens (or resumes) the session of a delivery of commits from the Workstation with its signed envelope.
    /// </summary>
    /// <param name="envelope">The envelope with the commits.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session with the content that is still missing.</returns>
    /// <exception cref="PatchRejectedException">The envelope was refused.</exception>
    Task<PatchSession> OpenCommitSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives the content of one file of an open session.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sha256">The SHA-256 hash the content must have.</param>
    /// <param name="content">The uncompressed content stream.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the content is stored.</returns>
    /// <exception cref="PatchRejectedException">The request was refused or the content does not match the manifest.</exception>
    Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the patch of an open session.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The processing result.</returns>
    /// <exception cref="PatchRejectedException">The request was refused or the content is incomplete.</exception>
    Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default);

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
    /// Gets the synchronization status of the local node without walking the working tree again.
    /// </summary>
    /// <remarks>
    /// The sync base and the changed files are those of the last full look (<see cref="GetStateAsync"/>, a sent or a
    /// received patch, a Pull); the times of the last exchange and the number of conflicts are current. The first call
    /// after the start does the full look.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The status.</returns>
    Task<NodeSyncStatus> GetSummaryAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent patches, newest first.
    /// </summary>
    /// <param name="count">The maximum number of patches.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The patches.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetRecentAsync(int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one page of the patches of the repository the node works with, newest first.
    /// </summary>
    /// <param name="skip">The number of the newest patches to skip.</param>
    /// <param name="count">The maximum number of patches.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The patches.</returns>
    Task<IReadOnlyList<PatchMetadata>> GetPageAsync(int skip, int count, CancellationToken cancellationToken = default);

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
    /// Takes a manual snapshot of the working tree as the sync base. Only for a working tree without Git: the base of a Git
    /// working tree is built from its commits automatically.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The recorded snapshot.</returns>
    /// <exception cref="SyncBridgeException">The working tree is a Git repository, is missing, or the snapshot failed.</exception>
    Task<SnapshotInfo> RebuildBaselineAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the patch file of the changed files of the working tree.
    /// </summary>
    /// <param name="paths">The relative paths of the files, or <see langword="null"/> for every changed file.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The bytes of the patch; empty when there is nothing to save.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree or no sync base, or Git failed.</exception>
    Task<byte[]> BuildPatchFileAsync(IReadOnlyCollection<string>? paths = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists the files a patch file changes.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The files, in the order of the patch.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree, or the bytes hold no patch or are too many.</exception>
    Task<IReadOnlyList<ChangedFile>> ReadPatchFileAsync(byte[] patch, CancellationToken cancellationToken = default);

    /// <summary>
    /// Brings a patch file into the working tree without committing it, or takes it out again.
    /// </summary>
    /// <param name="patch">The bytes of the patch.</param>
    /// <param name="paths">The files of the patch to use, or <see langword="null"/> for all of them.</param>
    /// <param name="reverse">Whether the changes are taken out instead of brought in.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>How it ended; when it failed, the working tree stayed as it was.</returns>
    /// <exception cref="SyncBridgeException">The node has no working tree, or the choice of files or the patch cannot be used.</exception>
    Task<PatchFileApplyResult> ApplyPatchFileAsync(byte[] patch, IReadOnlyCollection<string>? paths, bool reverse, CancellationToken cancellationToken = default);
}
