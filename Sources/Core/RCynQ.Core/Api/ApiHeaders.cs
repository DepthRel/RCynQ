namespace RCynQ.Core.Api;

/// <summary>
/// HTTP header and query parameter names of the local service API.
/// </summary>
public static class ApiHeaders
{
    /// <summary>
    /// The local management API token header used by the UI and CLI.
    /// </summary>
    public const string ApiToken = "X-Api-Token";

    /// <summary>
    /// The shared link token header sent by the peer to <c>POST /patch</c>.
    /// </summary>
    public const string LinkToken = "X-Link-Token";

    /// <summary>
    /// The sender node identifier header sent by the peer to <c>POST /patch</c>.
    /// </summary>
    public const string NodeId = "X-Node-Id";

    /// <summary>
    /// The query parameter carrying the API token for SignalR connections that cannot send headers.
    /// </summary>
    public const string AccessTokenQuery = "access_token";
}
