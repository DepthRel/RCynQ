namespace RCynQ.Core.Api;

/// <summary>
/// Limits of the local HTTP API that the service enforces and its clients plan around.
/// </summary>
public static class ApiLimits
{
    /// <summary>The most patches one request for the patch list returns.</summary>
    public const int MAX_PATCHES_PAGE = 500;
}
