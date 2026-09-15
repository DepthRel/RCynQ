using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Receives patch envelopes from the peer.
/// </summary>
/// <remarks>
/// The receiver validates the link token and sender, verifies the signature, enforces replay protection,
/// stores payload and metadata and makes the initial apply attempt.
/// </remarks>
public interface IPatchReceiver
{
    /// <summary>
    /// Processes a received envelope.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="context">The transport-level request facts.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The processing result (<c>Applied</c>, <c>ConflictPending</c> or <c>Failed</c>).</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="PatchRejectedException">The envelope was refused before it was stored.</exception>
    Task<PatchApplyResult> ReceiveAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default);
}
