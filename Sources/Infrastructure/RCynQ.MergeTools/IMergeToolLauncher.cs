using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.MergeTools;

/// <summary>
/// Starts a merge tool on the files of one merge.
/// </summary>
public interface IMergeToolLauncher
{
    /// <summary>
    /// Starts the tool; the caller waits for it to exit.
    /// </summary>
    /// <param name="tool">The tool.</param>
    /// <param name="files">The files of the merge.</param>
    /// <param name="labels">The titles of the panes.</param>
    /// <returns>The running tool. Disposing it releases the handle and leaves the tool running.</returns>
    /// <exception cref="SyncBridgeException">The tool is missing or could not be started.</exception>
    IRunningProcess Start(MergeTool tool, MergeToolFiles files, MergeToolLabels labels);
}
