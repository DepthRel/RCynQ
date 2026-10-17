using RCynQ.Core.Enums;

namespace RCynQ.Core.Api;

/// <summary>
/// Route templates of the local service API and helpers that build concrete paths.
/// </summary>
/// <remarks>
/// The service listens on <c>http://127.0.0.1:&lt;ListenPort&gt;</c> only. <see cref="HEALTH"/> and the routes below
/// <see cref="PATCH_SESSION_PREFIX"/> are the only routes without the local API token; the patch session routes are protected
/// by the link token and the signature of the manifest.
/// </remarks>
public static class ApiRoutes
{
    /// <summary>Liveness probe.</summary>
    public const string HEALTH = "/health";

    /// <summary>Node synchronization status.</summary>
    public const string STATE = "/state";

    /// <summary>Node synchronization status as of the last look at the working tree, without walking it again.</summary>
    public const string STATE_SUMMARY = "/state/summary";

    /// <summary>Opens a patch session with the signed, gzip-compressed manifest; used by the peer through the SSH forward or its TLS port.</summary>
    public const string PATCH_SESSIONS = "/patch/sessions";

    /// <summary>Uploads the gzip-compressed content of one file of a patch session, addressed by its SHA-256 hash.</summary>
    public const string PATCH_SESSION_CONTENT = "/patch/sessions/{id:guid}/content/{sha256}";

    /// <summary>Applies the patch of a session once all its content is uploaded.</summary>
    public const string PATCH_SESSION_COMMIT = "/patch/sessions/{id:guid}/commit";

    /// <summary>The path prefix shared by the patch session routes, which are the only routes open to the peer.</summary>
    public const string PATCH_SESSION_PREFIX = "/patch/sessions";

    /// <summary>Recent patches.</summary>
    public const string PATCHES = "/patches";

    /// <summary>One patch.</summary>
    public const string PATCH_BY_ID = "/patches/{id:guid}";

    /// <summary>Resolve a conflicting patch.</summary>
    public const string RESOLVE_PATCH = "/patches/{id:guid}/resolve";

    /// <summary>The unified diff of a patch.</summary>
    public const string PATCH_DIFF = "/patches/{id:guid}/diff";

    /// <summary>Build and send a patch to the chain endpoint.</summary>
    public const string SEND_PATCH = "/patches/send";

    /// <summary>Create the sync base from the working tree.</summary>
    public const string BASELINE = "/patches/baseline";

    /// <summary>Synchronization history.</summary>
    public const string HISTORY = "/history";

    /// <summary>Configured chain links.</summary>
    public const string LINKS = "/links";

    /// <summary>One chain link.</summary>
    public const string LINK_BY_ID = "/links/{id:guid}";

    /// <summary>Runtime state of all links.</summary>
    public const string LINK_STATUS = "/links/status";

    /// <summary>Start a link.</summary>
    public const string LINK_UP = "/links/{id:guid}/up";

    /// <summary>Stop a link.</summary>
    public const string LINK_DOWN = "/links/{id:guid}/down";

    /// <summary>The routes of the links that have any, with the outcome of their last check.</summary>
    public const string LINK_ROUTES = "/links/routes";

    /// <summary>Check the routes now and move every link to its best one.</summary>
    public const string LINK_ROUTES_CHECK = "/links/routes/check";

    /// <summary>Pin a route of a link by hand, or hand the choice back to the node.</summary>
    public const string LINK_ROUTE_SELECTION = "/links/{id:guid}/route";

    /// <summary>One route of a link.</summary>
    public const string LINK_ROUTE_BY_ID = "/links/{id:guid}/routes/{routeId:guid}";

    /// <summary>The preference of one route of a link.</summary>
    public const string LINK_ROUTE_PRIORITY = "/links/{id:guid}/routes/{routeId:guid}/priority";

    /// <summary>The query parameter of <see cref="CONFIG_IMPORT"/> that carries the decision of the import.</summary>
    public const string IMPORT_DECISION_PARAMETER = "decision";

    /// <summary>The query parameter of <see cref="CONFIG_IMPORT"/> that names the link whose peer is replaced.</summary>
    public const string IMPORT_LINK_PARAMETER = "link";

    /// <summary>Current branch and divergence from Origin.</summary>
    public const string GIT_BRANCH = "/git/branch";

    /// <summary>Fetch branches from Origin.</summary>
    public const string GIT_FETCH = "/git/fetch";

    /// <summary>Serves an incremental git bundle to the peer that asks for the branches of this repository (Origin, no SSH server needed).</summary>
    public const string GIT_BUNDLE = "/git/bundle";

    /// <summary>Fetch and fast-forward the current branch.</summary>
    public const string GIT_PULL = "/git/pull";

    /// <summary>
    /// Answers the counterpart endpoint that asks whether this node is ready to exchange patches with it. Like the patch
    /// session routes it needs no local API token: the link token and the identifier of the asking node protect it.
    /// </summary>
    public const string PEER_PING = "/peer/ping";

    /// <summary>
    /// Tells a Workstation which repositories this Origin offers. Protected like <see cref="PEER_PING"/>: by the link token
    /// and the identifier of the asking node.
    /// </summary>
    public const string PEER_REPOSITORIES = "/peer/repositories";

    /// <summary>
    /// Takes a node that enrolls with an invitation of this node (POST). It needs no local API token and no link token:
    /// the single-use ticket of the invitation protects it.
    /// </summary>
    public const string PEER_ENROLL = "/peer/enroll";

    /// <summary>The open invitations of this node (GET) and creating one (POST).</summary>
    public const string CONFIG_INVITATIONS = "/config/invitations";

    /// <summary>Revokes an open invitation (DELETE).</summary>
    public const string CONFIG_INVITATION_BY_ID = "/config/invitations/{id}";

    /// <summary>Joins the node that wrote an invitation (POST).</summary>
    public const string CONFIG_JOIN = "/config/join";

    /// <summary>The repositories of the node (GET) and adding one (POST).</summary>
    public const string REPOSITORIES = "/repositories";

    /// <summary>One repository of the node (DELETE removes it from the node; its files stay).</summary>
    public const string REPOSITORY_BY_NAME = "/repositories/{name}";

    /// <summary>Makes a repository the one the node works with (POST).</summary>
    public const string REPOSITORY_ACTIVATE = "/repositories/{name}/activate";

    /// <summary>Run diagnostics.</summary>
    public const string DIAGNOSTICS_RUN = "/diagnostics/run";

    /// <summary>Apply a confirmed safe fix.</summary>
    public const string DIAGNOSTICS_FIX = "/diagnostics/fix";

    /// <summary>Recent diagnostics runs.</summary>
    public const string DIAGNOSTICS_RECENT = "/diagnostics/recent";

    /// <summary>Local node configuration.</summary>
    public const string CONFIG = "/config";

    /// <summary>Export the public node configuration.</summary>
    public const string CONFIG_EXPORT = "/config/export";

    /// <summary>Import a peer configuration.</summary>
    public const string CONFIG_IMPORT = "/config/import";

    /// <summary>Change the node role in the configuration file.</summary>
    public const string CONFIG_ROLE = "/config/role";

    /// <summary>Probe the network and SSH environment to prefill an export.</summary>
    public const string CONFIG_EXPORT_PROBE = "/config/export/probe";

    /// <summary>Generate the node SSH key when none exists.</summary>
    public const string CONFIG_SSH_KEY = "/config/ssh-key";

    /// <summary>Allow the imported neighbours in the <c>authorized_keys</c> of the local SSH server.</summary>
    public const string SSH_AUTHORIZED_KEYS = "/ssh/authorized-keys";

    /// <summary>Gracefully stop the service (used by the UI that started it).</summary>
    public const string SERVICE_SHUTDOWN = "/service/shutdown";

    /// <summary>Removes the finished data of the node: old patches, history, diagnostics runs and backups (POST).</summary>
    public const string DATA_CLEANUP = "/data/cleanup";

    /// <summary>Recent log entries.</summary>
    public const string LOGS = "/logs";

    /// <summary>SignalR hub.</summary>
    public const string HUB = "/hub";

    /// <summary>
    /// Builds the content upload path of a patch session.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <param name="sha256">The SHA-256 hash (lowercase hex) of the content.</param>
    /// <returns>The path.</returns>
    public static string PatchSessionContentPath(Guid id, string sha256) => $"/patch/sessions/{id}/content/{sha256}";

    /// <summary>
    /// Builds the commit path of a patch session.
    /// </summary>
    /// <param name="id">The patch identifier.</param>
    /// <returns>The path.</returns>
    public static string PatchSessionCommitPath(Guid id) => $"/patch/sessions/{id}/commit";

    /// <summary>
    /// Builds the path of one repository.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <returns>The path.</returns>
    public static string RepositoryPath(string name) => $"/repositories/{Uri.EscapeDataString(name)}";

    /// <summary>
    /// Builds the path that makes a repository the one the node works with.
    /// </summary>
    /// <param name="name">The repository name.</param>
    /// <returns>The path.</returns>
    public static string RepositoryActivatePath(string name) => $"/repositories/{Uri.EscapeDataString(name)}/activate";

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

    /// <summary>
    /// Builds the path that chooses the route of a link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkRouteSelectionPath(Guid id) => $"/links/{id}/route";

    /// <summary>
    /// Builds the path of one route of a link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <param name="routeId">The route identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkRoutePath(Guid id, Guid routeId) => $"/links/{id}/routes/{routeId}";

    /// <summary>
    /// Builds the path of one open invitation.
    /// </summary>
    /// <param name="id">The ticket identifier of the invitation.</param>
    /// <returns>The path.</returns>
    public static string InvitationPath(Guid id) => $"/config/invitations/{id}";

    /// <summary>
    /// Builds the path of the preference of one route of a link.
    /// </summary>
    /// <param name="id">The link identifier.</param>
    /// <param name="routeId">The route identifier.</param>
    /// <returns>The path.</returns>
    public static string LinkRoutePriorityPath(Guid id, Guid routeId) => $"/links/{id}/routes/{routeId}/priority";

    /// <summary>
    /// Builds the path of an import that carries a decision.
    /// </summary>
    /// <param name="decision">The decision, or <see langword="null"/> for an import without one.</param>
    /// <param name="linkId">The link whose peer is replaced, or <see langword="null"/>.</param>
    /// <returns>The path with its query.</returns>
    public static string ConfigImportPath(ImportDecision? decision, Guid? linkId)
    {
        if (decision is null)
        {
            return CONFIG_IMPORT;
        }

        var path = $"{CONFIG_IMPORT}?{IMPORT_DECISION_PARAMETER}={decision}";
        return linkId is null ? path : $"{path}&{IMPORT_LINK_PARAMETER}={linkId}";
    }
}
