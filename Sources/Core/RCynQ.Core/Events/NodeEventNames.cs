namespace RCynQ.Core.Events;

/// <summary>
/// Names of the SignalR client methods used to publish node events.
/// </summary>
public static class NodeEventNames
{
    /// <summary>
    /// Published when a transport link changes state.
    /// </summary>
    public const string LINK_STATE_CHANGED = "LinkStateChanged";

    /// <summary>
    /// Published when a patch is sent, received or resolved.
    /// </summary>
    public const string PATCH_EVENT = "PatchEvent";

    /// <summary>
    /// Published when a diagnostics run completes.
    /// </summary>
    public const string DIAGNOSTICS_EVENT = "DiagnosticsEvent";

    /// <summary>
    /// Published when the repository of this node or of the node at the other end changes.
    /// </summary>
    public const string REPOSITORY_EVENT = "RepositoryEvent";

    /// <summary>
    /// Published when the routes of a link were checked or the route in use changed.
    /// </summary>
    public const string LINK_ROUTES_CHANGED = "LinkRoutesChanged";

    /// <summary>
    /// Published when a stage of joining with an invitation started or ended.
    /// </summary>
    public const string JOIN_PROGRESS = "JoinProgress";
}
