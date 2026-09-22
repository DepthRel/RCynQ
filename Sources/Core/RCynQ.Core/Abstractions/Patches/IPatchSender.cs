using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Sends patches to the counterpart endpoint through the chain.
/// </summary>
/// <remarks>
/// A patch is sent as a session: the signed, compressed manifest first, then the content of every file the receiver
/// does not have yet, one request per file, then a request that asks the receiver to apply the patch.
/// </remarks>
public interface IPatchSender
{
    /// <summary>
    /// Signs (when unsigned) and sends a patch.
    /// </summary>
    /// <param name="envelope">The envelope with the manifest.</param>
    /// <param name="content">The source of the file content named by the manifest.</param>
    /// <param name="chain">
    /// The chain ordered by priority: all links but the last are jump hosts, the last link is the endpoint.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The result reported by the receiver.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The chain is empty.</exception>
    /// <exception cref="SyncBridgeException">The patch could not be delivered or was refused.</exception>
    Task<PatchApplyResult> SendAsync(PatchEnvelope envelope, IPatchContent content, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default);
}
