using RCynQ.Core.Enums;

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
    public const string SECTION_NAME = "SyncBridge";

    /// <summary>
    /// The default configuration file name.
    /// </summary>
    public const string DEFAULT_FILE_NAME = "syncbridge.json";

    /// <summary>
    /// Gets or sets the local node identifier.
    /// </summary>
    public string NodeId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the role of the local node.
    /// </summary>
    public NodeRole Role { get; set; } = NodeRole.Workstation;

    /// <summary>
    /// Gets or sets the display name of the local node. Defaults to <see cref="NodeId"/> when empty.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the repositories of the node: the Git repositories an Origin serves, or the local working copies of
    /// a Workstation. A Relay has none.
    /// </summary>
    public List<RepositoryOptions> Repositories { get; set; } = new();

    /// <summary>
    /// Gets or sets the name of the repository the node works with. When empty, the first repository is used.
    /// </summary>
    public string? ActiveRepository { get; set; }

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
    /// Gets the directory names that are always excluded from working tree snapshots, whatever a node configures.
    /// </summary>
    /// <remarks>
    /// Every host builds its snapshot filter from these names together with <see cref="ExcludedDirectories"/>, so the
    /// service (which binds the configuration, appending to the list) and the CLI (which deserializes the file, replacing
    /// the list) exclude the same directories for the same file.
    /// </remarks>
    public static IReadOnlyList<string> DefaultExcludedDirectories { get; } = new[] { "bin", "obj", ".vs", ".idea", "node_modules" };

    /// <summary>
    /// Gets or sets directory names excluded from working tree snapshots (the <c>.git</c> directory is always excluded).
    /// </summary>
    /// <remarks>
    /// Names configured for a node are <b>added</b> to <see cref="DefaultExcludedDirectories"/>, they do not replace
    /// them, so excluding one extra directory never brings build output back into the snapshots. Names are matched at any
    /// depth, without regard to case. This is a second mechanism next to <see cref="RespectGitIgnore"/>, for directories
    /// no <c>.gitignore</c> describes.
    /// </remarks>
    public List<string> ExcludedDirectories { get; set; } = new(DefaultExcludedDirectories);

    /// <summary>
    /// Gets or sets a value indicating whether files and directories described by <c>.gitignore</c> files in the tree are
    /// left out of snapshots and patches. Nested <c>.gitignore</c> files are honoured; <c>.git/info/exclude</c> and the
    /// global Git ignore file are not read.
    /// </summary>
    public bool RespectGitIgnore { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether an Origin takes commits a Workstation sends and fast-forwards its checked out
    /// branch to them. Off by default: only then does RCynQ write into the repository of an Origin beyond the files of a patch.
    /// </summary>
    public bool AcceptCommits { get; set; }

    /// <summary>
    /// Gets or sets the maximum accepted age of a received patch, in minutes.
    /// </summary>
    public int PatchMaxAgeMinutes { get; set; } = 24 * 60;

    /// <summary>
    /// Gets or sets for how many days finished data is kept: patch records with their diffs and content, history,
    /// diagnostics runs and backups of overwritten files. <c>0</c> keeps everything until it is cleaned by hand.
    /// </summary>
    /// <remarks>
    /// A patch that waits for a decision about a conflict is never removed by its age.
    /// </remarks>
    public int RetentionDays { get; set; } = DEFAULT_RETENTION_DAYS;

    /// <summary>
    /// The number of days finished data is kept unless the node configures another one.
    /// </summary>
    public const int DEFAULT_RETENTION_DAYS = 10;

    /// <summary>
    /// Gets or sets the SSH client settings.
    /// </summary>
    public SshClientOptions Ssh { get; set; } = new();

    /// <summary>
    /// Gets or sets how neighbours reach the patch endpoint of this node.
    /// </summary>
    public EndpointOptions Endpoint { get; set; } = new();

    /// <summary>
    /// Gets or sets the chain links.
    /// </summary>
    public List<ChainLinkOptions> Chain { get; set; } = new();
}
