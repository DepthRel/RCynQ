namespace RCynQ.Core.Api;

/// <summary>
/// What a cleanup of the stored data of a node removed.
/// </summary>
/// <param name="Patches">The number of patch records removed, with their diffs and content.</param>
/// <param name="HistoryEntries">The number of history entries removed.</param>
/// <param name="DiagnosticRuns">The number of stored diagnostics runs removed.</param>
/// <param name="Backups">The number of backups of overwritten files removed.</param>
/// <param name="TemporaryItems">The number of leftover temporary files and directories removed.</param>
/// <param name="FreedBytes">The disk space that was freed, in bytes.</param>
public sealed record DataCleanupReport(int Patches, int HistoryEntries, int DiagnosticRuns, int Backups, int TemporaryItems, long FreedBytes)
{
    /// <summary>
    /// Gets a value indicating whether anything was removed.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool RemovedAnything => Patches + HistoryEntries + DiagnosticRuns + Backups + TemporaryItems > 0;
}
