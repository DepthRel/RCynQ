using RCynQ.UI.Enums;

namespace RCynQ.UI.Services.Updates;

/// <summary>
/// What a check for a new version found.
/// </summary>
/// <param name="Status">The outcome.</param>
/// <param name="Latest">The latest published version, or <see langword="null"/> when the check failed.</param>
/// <param name="Error">Why the check failed, in the language of the interface, or <see langword="null"/>.</param>
public sealed record UpdateCheckResult(UpdateCheckStatus Status, Version? Latest, string? Error);
