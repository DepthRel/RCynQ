using RCynQ.Core.Config;

namespace RCynQ.Core.Diagnostics;

/// <summary>
/// The facts a diagnostic check may inspect.
/// </summary>
/// <param name="Node">The local node.</param>
/// <param name="Options">The node options.</param>
/// <param name="Chain">The configured chain ordered by priority.</param>
/// <param name="Paths">The application data paths.</param>
public sealed record DiagnosticContext(NodeConfig Node, SyncBridgeOptions Options, IReadOnlyList<ChainLinkConfig> Chain, AppDataPaths Paths);
