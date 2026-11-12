namespace RCynQ.Core.Config;

/// <summary>
/// What a reset of the node removes, worked out before anything is removed, so it can be shown to the user first.
/// </summary>
/// <param name="NodeId">The identifier of the node: the word the user types to confirm the reset.</param>
/// <param name="ConfigPath">The configuration file of the node, removed last.</param>
/// <param name="DataRoot">The data directory of the node.</param>
/// <param name="HasData">
/// Whether <paramref name="DataRoot"/> holds the data of a node (its database, its keys or its installation stamp); a
/// folder that does not is left as it is.
/// </param>
/// <param name="Entries">The files and folders of the node in <paramref name="DataRoot"/> that exist and are removed.</param>
public sealed record NodeResetPlan(string NodeId, string ConfigPath, string DataRoot, bool HasData, IReadOnlyList<string> Entries);
