using RCynQ.Core.Config;

namespace RCynQ.Core.Abstractions.Storage;

/// <summary>
/// Removes the configuration and the data of a node, so the next start begins with the first setup, as on a computer
/// the node never ran on. The repositories and working folders of the user are never touched.
/// </summary>
public interface INodeDataEraser
{
    /// <summary>
    /// Works out what a reset removes, without removing anything.
    /// </summary>
    /// <param name="options">The options of the node, read from its configuration file.</param>
    /// <param name="configPath">The configuration file.</param>
    /// <param name="portableRoot">The root of a portable installation, or <see langword="null"/>.</param>
    /// <returns>The plan.</returns>
    NodeResetPlan Plan(SyncBridgeOptions options, string configPath, string? portableRoot);

    /// <summary>
    /// Removes what a plan names. The service of the node must not run: it holds its database open.
    /// </summary>
    /// <param name="plan">The plan from <see cref="Plan"/>.</param>
    /// <returns>
    /// What was removed. The configuration file is removed only when everything else was, so a reset that stopped half
    /// way can be run again.
    /// </returns>
    NodeResetReport Erase(NodeResetPlan plan);
}
