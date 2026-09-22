using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;

namespace RCynQ.Core.Abstractions.Patches;

/// <summary>
/// Resolves file conflicts produced while applying a patch.
/// </summary>
public interface IConflictResolver
{
    /// <summary>
    /// Resolves the given conflicts according to the configured policy.
    /// </summary>
    /// <param name="envelope">The patch envelope that produced the conflicts. Must not be <see langword="null"/>.</param>
    /// <param name="content">The stored content of the patch. Must not be <see langword="null"/>.</param>
    /// <param name="conflicts">
    /// The files that produced conflicts. Must not be <see langword="null"/>.
    /// </param>
    /// <param name="policy">The conflict resolution policy to apply. Must be a defined <see cref="ConflictPolicy"/>.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that resolves to the final application result.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="policy"/> is not a defined member.</exception>
    /// <exception cref="PatchApplyException">Conflict resolution failed.</exception>
    Task<PatchApplyResult> ResolveAsync(
        PatchEnvelope envelope,
        IPatchContent content,
        IReadOnlyList<ConflictFile> conflicts,
        ConflictPolicy policy,
        CancellationToken cancellationToken = default);
}
