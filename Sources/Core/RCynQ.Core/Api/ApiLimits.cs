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

    /// <summary>The largest patch file the service builds, reads or applies, in bytes.</summary>
    public const long MAX_PATCH_FILE_BYTES = 256L * 1024 * 1024;

    /// <summary>
    /// The largest Git bundle a Workstation takes from Origin, in bytes: room for the whole history of a large
    /// repository, yet a bound for an Origin that never stops sending.
    /// </summary>
    public const long MAX_GIT_BUNDLE_BYTES = 16L * 1024 * 1024 * 1024;

    /// <summary>The longest answer of a peer that refused a request, in bytes, that is read for its reason.</summary>
    public const int MAX_REFUSAL_BYTES = 64 * 1024;
}
