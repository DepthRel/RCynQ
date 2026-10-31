using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Exceptions;

namespace RCynQ.MergeTools;

/// <summary>
/// Starts the process of a merge tool: a window of the user, not a command whose output is read.
/// </summary>
public interface IMergeToolProcessStarter
{
    /// <summary>
    /// Starts the process.
    /// </summary>
    /// <param name="spec">The executable, the arguments and the working folder; the timeout is not used.</param>
    /// <returns>The running process. Disposing it releases the handle and leaves the tool running.</returns>
    /// <exception cref="SyncBridgeException">The process could not be started.</exception>
    IRunningProcess Start(ProcessSpec spec);
}
