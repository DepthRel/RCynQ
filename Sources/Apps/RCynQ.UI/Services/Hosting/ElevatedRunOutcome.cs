namespace RCynQ.UI.Services.Hosting;

/// <summary>
/// The outcome of running a command with administrator rights.
/// </summary>
public enum ElevatedRunOutcome
{
    /// <summary>The command ran and succeeded.</summary>
    Completed = 1,

    /// <summary>The user declined the Windows confirmation.</summary>
    Cancelled = 2,

    /// <summary>Elevation is not available (not Windows, or the CLI was not found).</summary>
    NotAvailable = 3,

    /// <summary>The command failed.</summary>
    Failed = 4
}
