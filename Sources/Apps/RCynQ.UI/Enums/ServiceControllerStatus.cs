namespace RCynQ.UI.Enums;

/// <summary>
/// The state of the local RCynQ service as seen by the UI.
/// </summary>
public enum ServiceControllerStatus
{
    /// <summary>The service has not been checked yet.</summary>
    NotStarted = 1,

    /// <summary>The UI is starting the service.</summary>
    Starting = 2,

    /// <summary>The service was started by this UI and runs.</summary>
    Running = 3,

    /// <summary>A service started outside the UI answers; it is used but not stopped by the UI.</summary>
    Attached = 4,

    /// <summary>The service started by this UI was stopped.</summary>
    Stopped = 5,

    /// <summary>The service could not be started.</summary>
    Failed = 6
}
