using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Sends patch envelopes to the counterpart endpoint through the chain.
/// </summary>
public interface IPatchSender
{
    /// <summary>
    /// Signs (when unsigned) and sends an envelope.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="chain">
    /// The chain ordered by priority: all links but the last are jump hosts, the last link is the endpoint.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result reported by the receiver.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The chain is empty.</exception>
    /// <exception cref="SyncBridgeException">The envelope could not be delivered or was refused.</exception>
    Task<PatchApplyResult> SendAsync(PatchEnvelope envelope, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default);
}
