using RCynQ.Core.Patches;
using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Merging;

/// <summary>
/// How a merge in a merge tool ended.
/// </summary>
/// <param name="Kind">How it ended.</param>
/// <param name="Result">The result of the service, for <see cref="ExternalMergeOutcomeKind.Completed"/>.</param>
/// <param name="Unmergeable">The files no merge tool can merge, for <see cref="ExternalMergeOutcomeKind.Unmergeable"/>.</param>
/// <param name="Message">Why the merge failed, for <see cref="ExternalMergeOutcomeKind.Failed"/>.</param>
/// <param name="FilesWithMarkers">The files the user took with conflict markers left in them.</param>
public sealed record ExternalMergeOutcome(
    ExternalMergeOutcomeKind Kind,
    PatchApplyResult? Result,
    IReadOnlyList<ConflictFile> Unmergeable,
    string? Message,
    IReadOnlyList<string> FilesWithMarkers)
{
    /// <summary>Gets the outcome of a merge the user gave up.</summary>
    public static ExternalMergeOutcome Cancelled { get; } = new(ExternalMergeOutcomeKind.Cancelled, null, Array.Empty<ConflictFile>(), null, Array.Empty<string>());

    /// <summary>
    /// Creates the outcome of a merge that failed.
    /// </summary>
    /// <param name="message">Why.</param>
    /// <returns>The outcome.</returns>
    public static ExternalMergeOutcome Failed(string message) => new(ExternalMergeOutcomeKind.Failed, null, Array.Empty<ConflictFile>(), message, Array.Empty<string>());
}
