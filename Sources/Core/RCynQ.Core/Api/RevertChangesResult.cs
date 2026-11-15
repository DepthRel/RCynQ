using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Answer of taking back the changes of one file of the working tree.
/// </summary>
/// <param name="Path">The path relative to the working tree.</param>
/// <param name="Outcome">Whether the file was restored, deleted or had nothing to take back.</param>
public sealed record RevertChangesResult(string Path, WorkingTreeRevertOutcome Outcome);
