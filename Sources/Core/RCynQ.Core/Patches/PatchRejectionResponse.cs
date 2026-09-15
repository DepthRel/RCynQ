using RCynQ.Core.Enums;

namespace RCynQ.Core.Patches;

/// <summary>
/// The error body returned by the patch endpoint when an envelope is refused.
/// </summary>
/// <param name="Reason">The rejection reason.</param>
/// <param name="Error">A human-readable explanation.</param>
public sealed record PatchRejectionResponse(PatchRejectionReason Reason, string Error);
