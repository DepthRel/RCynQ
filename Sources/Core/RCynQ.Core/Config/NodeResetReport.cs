namespace RCynQ.Core.Config;

/// <summary>
/// What a reset of the node removed.
/// </summary>
/// <param name="Removed">The number of files and folders of the node that were removed, the configuration file included.</param>
/// <param name="Failed">The files and folders that could not be removed; empty when the reset is complete.</param>
public sealed record NodeResetReport(int Removed, IReadOnlyList<string> Failed)
{
    /// <summary>
    /// Gets a value indicating whether everything of the node was removed, the configuration file included.
    /// </summary>
    public bool IsComplete => Failed.Count == 0;
}
