namespace RCynQ.Core.Api;

/// <summary>
/// HTTP header and query parameter names of the local service API.
/// </summary>
public static class ApiHeaders
{
    /// <summary>
    /// The local management API token header used by the UI and CLI.
    /// </summary>
    public const string API_TOKEN = "X-Api-Token";

    /// <summary>
    /// The shared link token header sent by the peer to <c>POST /patch</c>.
    /// </summary>
    public const string LINK_TOKEN = "X-Link-Token";

    /// <summary>
    /// The sender node identifier header sent by the peer to <c>POST /patch</c>.
    /// </summary>
    public const string NODE_ID = "X-Node-Id";

    /// <summary>The header of a bundle response that names the branch the served repository has checked out.</summary>
    public const string GIT_HEAD = "X-Git-Head";

    /// <summary>
    /// The name of the repository the asking node works with, sent with a ping so the answering node learns of a switch.
    /// </summary>
    public const string REPOSITORY = "X-Repository";

    /// <summary>
    /// The query parameter carrying the API token for SignalR connections that cannot send headers.
    /// </summary>
    public const string ACCESS_TOKEN_QUERY = "access_token";
}
