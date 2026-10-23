namespace RCynQ.Core.Api;

/// <summary>
/// Limits of the local HTTP API that the service enforces and its clients plan around.
/// </summary>
public static class ApiLimits
{
    /// <summary>The most patches one request for the patch list returns.</summary>
    public const int MAX_PATCHES_PAGE = 500;

    /// <summary>The most branches Origin lists in one answer; a Workstation ignores the rest.</summary>
    public const int MAX_GIT_BRANCHES = 10000;

    /// <summary>The most commits one delivery of commits carries and the list of unsent commits shows.</summary>
    public const int MAX_DELIVERY_COMMITS = 1000;
}
