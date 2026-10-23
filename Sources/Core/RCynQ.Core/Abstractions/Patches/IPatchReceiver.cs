using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Receives patches from the peer.
/// </summary>
/// <remarks>
/// A patch arrives as a session. <see cref="OpenSessionAsync"/> validates the link token, the sender, the signature
/// and the replay protection and stores the manifest; <see cref="ReceiveContentAsync"/> accepts the content of one file
/// and verifies it against the signed manifest; <see cref="CommitAsync"/> makes the apply attempt once all content is
/// present. Nothing touches the working tree before the commit.
/// </remarks>
public interface IPatchReceiver
{
    /// <summary>
    /// Validates a manifest and opens (or resumes) the session of the patch.
    /// </summary>
    /// <param name="envelope">The envelope with the manifest.</param>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session with the content that is still missing.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The envelope was refused before anything was stored.</exception>
    Task<PatchSession> OpenSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens (or resumes) the session of a delivery of commits: the same checks as for a patch, and that this Origin takes
    /// commits and has the branch they are built on checked out at the commit they are built on.
    /// </summary>
    /// <param name="envelope">The envelope with the commits.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The session with the content that is still missing.</returns>
    /// <exception cref="PatchRejectedException">The envelope was refused.</exception>
    Task<PatchSession> OpenCommitSessionAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Accepts the content of one file of an open session.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="sha256">The SHA-256 hash (lowercase hex) the content must have.</param>
    /// <param name="content">The uncompressed content stream.</param>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the content is stored.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The session is unknown, the hash is not part of the manifest or the content does not match it.</exception>
    Task ReceiveContentAsync(Guid patchId, string sha256, Stream content, PatchReceiveContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the patch of an open session once its content is complete.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The processing result (<c>Applied</c>, <c>ConflictPending</c> or <c>Failed</c>).</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The session is unknown, already finished or its content is incomplete.</exception>
    Task<PatchApplyResult> CommitAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default);
}
