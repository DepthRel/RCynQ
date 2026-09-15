using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Config;

/// <summary>
/// Bindable node configuration, read from <c>syncbridge.json</c>, environment variables or the command line.
/// </summary>
/// <remarks>
/// This type is a mutable configuration model only. Use <see cref="SyncBridgeOptionsValidator"/> to validate
/// it and to map it to the immutable domain records <see cref="NodeConfig"/> and <see cref="ChainLinkConfig"/>.
/// </remarks>
public sealed class SyncBridgeOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "SyncBridge";

    /// <summary>
    /// The default configuration file name.
    /// </summary>
    public const string DefaultFileName = "syncbridge.json";

    /// <summary>
    /// Gets or sets the local node identifier.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role of the local node.
    /// </summary>
    public NodeRole Role { get; set; } = NodeRole.Source;

    /// <summary>
    /// Gets or sets the display name of the local node. Defaults to <see cref="NodeId"/> when empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Git repository path (Dest).
    /// </summary>
    public string? RepoPath { get; set; }

    /// <summary>
    /// Gets or sets the working directory path (Source; optional for Dest).
    /// </summary>
    public string? WorkdirPath { get; set; }

    /// <summary>
    /// Gets or sets the loopback port of the local service (patch endpoint, API and SignalR hub).
    /// </summary>
    public int ListenPort { get; set; } = 18443;

    /// <summary>
    /// Gets or sets the shared link token expected in the <c>X-Link-Token</c> header and sent to the peer.
    /// When empty the token is read from the secret store entry <c>link-token</c>.
    /// </summary>
    public string? LinkToken { get; set; }

    /// <summary>
    /// Gets or sets the application data root. Defaults to the per-user local application data folder.
    /// </summary>
    public string? DataRoot { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether database migrations run automatically on start.
    /// </summary>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>
    /// Gets or sets directory names excluded from working tree snapshots (the <c>.git</c> directory is always excluded).
    /// </summary>
    public List<string> ExcludedDirectories { get; set; } = new() { "bin", "obj", ".vs", ".idea", "node_modules" };

    /// <summary>
    /// Gets or sets the maximum accepted age of a received patch, in minutes.
    /// </summary>
    public int PatchMaxAgeMinutes { get; set; } = 24 * 60;

    /// <summary>
    /// Gets or sets the SSH client settings.
    /// </summary>
    public SshClientOptions Ssh { get; set; } = new();

    /// <summary>
    /// Gets or sets the chain links.
    /// </summary>
    public List<ChainLinkOptions> Chain { get; set; } = new();
}
