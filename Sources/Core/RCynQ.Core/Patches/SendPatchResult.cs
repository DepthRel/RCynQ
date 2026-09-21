namespace RCynQ.Core.Patches;

/// <summary>
/// The outcome of a send request.
/// </summary>
/// <param name="Sent">Whether a patch was built and a delivery was attempted.</param>
/// <param name="PatchId">The patch identifier, or <see langword="null"/> when nothing was sent.</param>
/// <param name="Result">The result reported by the peer, or <see langword="null"/> when nothing was sent.</param>
/// <param name="Message">A human-readable summary.</param>
public sealed record SendPatchResult(bool Sent, Guid? PatchId, PatchApplyResult? Result, string Message);
