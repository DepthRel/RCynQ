namespace RCynQ.UI.Enums;

/// <summary>
/// What a check for a new version of the application found.
/// </summary>
public enum UpdateCheckStatus
{
    /// <summary>The application is not checked yet.</summary>
    Unknown = 0,

    /// <summary>The running version is the latest one, or newer.</summary>
    UpToDate = 1,

    /// <summary>A newer version is published.</summary>
    Available = 2,

    /// <summary>The list of releases could not be read.</summary>
    Failed = 3,
}
