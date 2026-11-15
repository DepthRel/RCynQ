using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Answer of a choice of the changes of a file.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="Outcome">Whether the whole file, a part of it or nothing goes.</param>
public sealed record PartialChoiceResult(string Path, PartialChoiceOutcome Outcome);
