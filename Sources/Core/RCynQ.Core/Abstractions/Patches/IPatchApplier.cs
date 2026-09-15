using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Applies patch envelopes to a target directory.
/// </summary>
public interface IPatchApplier
{
    /// <summary>
    /// Applies the given patch envelope to the target directory.
    /// </summary>
    /// <param name="envelope">The patch envelope to apply. Must not be <see langword="null"/>.</param>
    /// <param name="targetDirectory">The path of the directory to apply the patch to.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The application result, including applied file counts and any conflicts.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="envelope"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="targetDirectory"/> is blank.</exception>
    /// <exception cref="PatchApplyException">Applying the patch failed.</exception>
    Task<PatchApplyResult> ApplyAsync(
        PatchEnvelope envelope,
        string targetDirectory,
        CancellationToken cancellationToken = default);
}
