using RCynQ.Core.Enums;
using RCynQ.Core.State;

namespace RCynQ.Core.Events;

/// <summary>
/// Names of the SignalR client methods used to publish node events.
/// </summary>
public static class NodeEventNames
{
    /// <summary>
    /// Published when a transport link changes state.
    /// </summary>
    public const string LinkStateChanged = "LinkStateChanged";

    /// <summary>
    /// Published when a patch is sent, received or resolved.
    /// </summary>
    public const string PatchEvent = "PatchEvent";

    /// <summary>
    /// Published when a diagnostics run completes.
    /// </summary>
    public const string DiagnosticsEvent = "DiagnosticsEvent";
}
