namespace RCynQ.MergeTools;

/// <summary>
/// Finds the merge tools installed on the machine.
/// </summary>
public interface IMergeToolLocator
{
    /// <summary>
    /// Looks for every tool of the catalog.
    /// </summary>
    /// <param name="cancellationToken">A token to stop looking.</param>
    /// <returns>The tools found and the one to offer first.</returns>
    Task<MergeToolScan> ScanAsync(CancellationToken cancellationToken = default);
}
