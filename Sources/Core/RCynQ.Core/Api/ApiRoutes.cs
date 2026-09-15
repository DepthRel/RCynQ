namespace RCynQ.Core.Api;

/// <summary>
/// Route templates of the local service API and helpers that build concrete paths.
/// </summary>
/// <remarks>
/// The service listens on <c>http://127.0.0.1:&lt;ListenPort&gt;</c> only. <see cref="Health"/> and <see cref="Patch"/>
/// are the only routes without the local API token; <see cref="Patch"/> is protected by the link token and signature.
/// </remarks>
public static class ApiRoutes
{
    /// <summary>Liveness probe.</summary>
    public const string Health = "/health";

    /// <summary>Node synchronization status.</summary>
    public const string State = "/state";

    /// <summary>Patch receive endpoint used by the peer through the SSH forward.</summary>
    public const string Patch = "/patch";

    /// <summary>Recent patches.</summary>
    public const string Patches = "/patches";

    /// <summary>One patch.</summary>
    public const string PatchById = "/patches/{id:guid}";

    /// <summary>Resolve a conflicting patch.</summary>
    public const string ResolvePatch = "/patches/{id:guid}/resolve";

    /// <summary>The unified diff of a patch.</summary>
    public const string PatchDiff = "/patches/{id:guid}/diff";

    /// <summary>Build and send a patch to the chain endpoint.</summary>
    public const string SendPatch = "/patches/send";

    /// <summary>Create the sync base from the working tree.</summary>
    public const string Baseline = "/patches/baseline";

    /// <summary>Synchronization history.</summary>
    public const string History = "/history";

    /// <summary>Configured chain links.</summary>
    public const string Links = "/links";

    /// <summary>One chain link.</summary>
    public const string LinkById = "/links/{id:guid}";

    /// <summary>Runtime state of all links.</summary>
    public const string LinkStatus = "/links/status";

    /// <summary>Start a link.</summary>
    public const string LinkUp = "/links/{id:guid}/up";

    /// <summary>Stop a link.</summary>
    public const string LinkDown = "/links/{id:guid}/down";

    /// <summary>Current branch and divergence from Dest.</summary>
    public const string GitBranch = "/git/branch";

    /// <summary>Fetch branches from Dest.</summary>
    public const string GitFetch = "/git/fetch";

    /// <summary>Fetch and fast-forward the current branch.</summary>
    public const string GitPull = "/git/pull";

    /// <summary>Run diagnostics.</summary>
    public const string DiagnosticsRun = "/diagnostics/run";

    /// <summary>Apply a confirmed safe fix.</summary>
    public const string DiagnosticsFix = "/diagnostics/fix";

    /// <summary>Recent diagnostics runs.</summary>
    public const string DiagnosticsRecent = "/diagnostics/recent";

    /// <summary>Local node configuration.</summary>
    public const string Config = "/config";

    /// <summary>Export the public node configuration.</summary>
    public const string ConfigExport = "/config/export";

    /// <summary>Import a peer configuration.</summary>
    public const string ConfigImport = "/config/import";

    /// <summary>Change the node role in the configuration file.</summary>
    public const string ConfigRole = "/config/role";

    /// <summary>Probe the network and SSH environment to prefill an export.</summary>
    public const string ConfigExportProbe = "/config/export/probe";

    /// <summary>Generate the node SSH key when none exists.</summary>
    public const string ConfigSshKey = "/config/ssh-key";

    /// <summary>Allow the imported neighbours in the <c>authorized_keys</c> of the local SSH server.</summary>
    public const string SshAuthorizedKeys = "/ssh/authorized-keys";

    /// <summary>Gracefully stop the service (used by the UI that started it).</summary>
    public const string ServiceShutdown = "/service/shutdown";

    /// <summary>Recent log entries.</summary>
    public const string Logs = "/logs";

    /// <summary>SignalR hub.</summary>
    public const string Hub = "/hub";

    /// <summary>
    /// Builds the path of one patch.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <returns>The path.</returns>
    public static string PatchPath(Guid id) => $"/patches/{id}";

    /// <summary>
    /// Builds the resolve path of a patch.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <returns>The path.</returns>
    public static string ResolvePatchPath(Guid id) => $"/patches/{id}/resolve";

    /// <summary>
    /// Builds the diff path of a patch.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <returns>The path.</returns>
    public static string PatchDiffPath(Guid id) => $"/patches/{id}/diff";

    /// <summary>
    /// Builds the path of one link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkPath(Guid id) => $"/links/{id}";

    /// <summary>
    /// Builds the start path of a link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkUpPath(Guid id) => $"/links/{id}/up";

    /// <summary>
    /// Builds the stop path of a link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkDownPath(Guid id) => $"/links/{id}/down";
}
