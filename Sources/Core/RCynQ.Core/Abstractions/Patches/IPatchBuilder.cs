using RCynQ.Core.Ids;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Builds complete patch envelopes from two directory states.
/// </summary>
public interface IPatchBuilder
{
    /// <summary>
    /// Builds a patch envelope from the base directory to the incoming directory.
    /// </summary>
    /// <param name="sourceNode">The node that produces the patch.</param>
    /// <param name="targetNode">The node the patch is addressed to.</param>
    /// <param name="baseDirectory">The path of the base (outgoing) directory.</param>
    /// <param name="incomingDirectory">The path of the incoming directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The complete patch envelope.</returns>
    /// <exception cref="ArgumentException">A directory path is blank.</exception>
    Task<PatchEnvelope> BuildAsync(
        NodeId sourceNode,
        NodeId targetNode,
        string baseDirectory,
        string incomingDirectory,
        CancellationToken cancellationToken = default);
}
