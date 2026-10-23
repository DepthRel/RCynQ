using RCynQ.Core.Enums;
using RCynQ.Core.Patches;
using RCynQ.Localization;

namespace RCynQ.Sync.Commits;

/// <summary>
/// Words the history entry of a received delivery, so the history says that commits arrived and how many.
/// </summary>
internal static class CommitHistoryText
{
    /// <summary>
    /// Gets the details of the history entry of a received patch.
    /// </summary>
    /// <param name="envelope">The received patch.</param>
    /// <param name="result">The outcome.</param>
    /// <returns>The encoded text for applied commits; <see langword="null"/> otherwise, so the entry shows the error.</returns>
    public static string? Received(PatchEnvelope envelope, PatchApplyResult result)
        => envelope.Commits is { } commits && result.Status == PatchStatus.Applied
            ? LocalizedText.Encode("Commits.Received", commits.Commits.Count, commits.Branch)
            : null;
}
