using RCynQ.Core.Enums;
using RCynQ.Core.Validation;

namespace RCynQ.Core.Patches;

/// <summary>
/// The outcome of processing or resolving a patch.
/// </summary>
public sealed record PatchApplyResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchApplyResult"/> record.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="status">The resulting patch status.</param>
    /// <param name="appliedFileCount">The number of files written to the working tree.</param>
    /// <param name="conflicts">The conflicting files; empty when there are none.</param>
    /// <param name="error">A human-readable error, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="conflicts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="status"/> is not defined.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="appliedFileCount"/> is negative.</exception>
    public PatchApplyResult(
        Guid patchId,
        PatchStatus status,
        int appliedFileCount,
        IReadOnlyList<ConflictFile> conflicts,
        string? error)
    {
        PatchId = patchId;
        Status = Guard.Defined(status, nameof(status));
        AppliedFileCount = Guard.NonNegative(appliedFileCount, nameof(appliedFileCount));
        Conflicts = Guard.NotNull(conflicts, nameof(conflicts));
        Error = error;
    }

    /// <summary>
    /// Gets the patch identifier.
    /// </summary>
    public Guid PatchId { get; }

    /// <summary>
    /// Gets the resulting patch status.
    /// </summary>
    public PatchStatus Status { get; }

    /// <summary>
    /// Gets the number of files written to the working tree.
    /// </summary>
    public int AppliedFileCount { get; }

    /// <summary>
    /// Gets the conflicting files.
    /// </summary>
    public IReadOnlyList<ConflictFile> Conflicts { get; }

    /// <summary>
    /// Gets a human-readable error, or <see langword="null"/>.
    /// </summary>
    public string? Error { get; }

    /// <summary>
    /// Gets a value indicating whether the patch ended in a final successful state.
    /// </summary>
    public bool Succeeded => Status is PatchStatus.Applied or PatchStatus.ConflictResolved;

    /// <summary>
    /// Gets a value indicating whether the result contains conflicts.
    /// </summary>
    public bool HasConflicts => Conflicts.Count > 0;

    /// <summary>
    /// Creates a result for a cleanly applied patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="appliedFileCount">The number of applied files.</param>
    /// <returns>The result.</returns>
    public static PatchApplyResult Applied(Guid patchId, int appliedFileCount)
        => new(patchId, PatchStatus.Applied, appliedFileCount, Array.Empty<ConflictFile>(), null);

    /// <summary>
    /// Creates a result for a patch waiting for conflict resolution.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="conflicts">The conflicting files.</param>
    /// <param name="appliedFileCount">The number of files already written.</param>
    /// <returns>The result.</returns>
    public static PatchApplyResult Conflict(Guid patchId, IReadOnlyList<ConflictFile> conflicts, int appliedFileCount = 0)
        => new(patchId, PatchStatus.ConflictPending, appliedFileCount, conflicts, null);

    /// <summary>
    /// Creates a result for a failed patch.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="error">The error description.</param>
    /// <returns>The result.</returns>
    public static PatchApplyResult Failed(Guid patchId, string error)
        => new(patchId, PatchStatus.Failed, 0, Array.Empty<ConflictFile>(), error);
}
