namespace RCynQ.UI.Enums;

/// <summary>
/// The outcome of an attempt to make the local service available.
/// </summary>
public enum ServiceStartOutcome
{
    /// <summary>The UI started the service and it answers.</summary>
    Started = 1,

    /// <summary>A service was already running and answers.</summary>
    AlreadyRunning = 2,

    /// <summary>The configuration file does not exist.</summary>
    NotConfigured = 3,

    /// <summary>The service executable was not found.</summary>
    NotFound = 4,

    /// <summary>The service exited or did not answer in time.</summary>
    Failed = 5,

    /// <summary>The running service was not started by the UI, so the UI cannot restart it.</summary>
    NotManaged = 6
}
