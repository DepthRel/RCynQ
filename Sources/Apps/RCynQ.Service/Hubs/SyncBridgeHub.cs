using Microsoft.AspNetCore.SignalR;

namespace RCynQ.Service.Hubs;

/// <summary>
/// SignalR hub at <c>/hub</c>. Clients only listen; the server pushes <c>LinkStateChanged</c>, <c>PatchEvent</c> and
/// <c>DiagnosticsEvent</c> (see <c>NodeEventNames</c>).
/// </summary>
public sealed class SyncBridgeHub : Hub
{
}
