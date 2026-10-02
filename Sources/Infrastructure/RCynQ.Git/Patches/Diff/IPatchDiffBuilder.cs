using RCynQ.Core.Abstractions.Patches;
using RCynQ.Core.Patches;

namespace RCynQ.Git.Patches.Diff;

/// <summary>
/// Builds the unified diff of a patch on demand from a base directory and the incoming content.
/// </summary>
/// <remarks>
/// The diff is never transferred between nodes: the sender and the receiver each build it locally, the receiver from its own
/// sync base, which by design equals the sender's base.
/// </remarks>
public interface IPatchDiffBuilder
{
    /// <summary>
    /// Builds the diff of a manifest.
    /// </summary>
    /// <param name="manifest">The file changes.</param>
    /// <param name="baseDirectory">The directory with the old state of modified and deleted files.</param>
    /// <param name="content">The source of the incoming content of added and modified files.</param>
    /// <param name="limits">The size limits; <see cref="PatchDiffLimits.Full"/> for a diff <c>git apply</c> can use.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The diff bytes; empty when nothing differs.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="RCynQ.Core.Exceptions.GitExecutionException">git reported an error.</exception>
    Task<byte[]> BuildAsync(
        IReadOnlyList<FileChange> manifest,
        string baseDirectory,
        IPatchContent content,
        PatchDiffLimits limits,
        CancellationToken cancellationToken = default);
}
