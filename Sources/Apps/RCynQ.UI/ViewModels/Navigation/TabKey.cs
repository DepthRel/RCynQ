namespace RCynQ.UI.ViewModels.Navigation;

/// <summary>
/// Identifies the tabs of the main window.
/// </summary>
public enum TabKey
{
    /// <summary>Overview of links, last sync and problems.</summary>
    Overview = 1,

    /// <summary>Role, paths and chain nodes.</summary>
    RoleAndNodes = 2,

    /// <summary>Live patch synchronization (Source and Dest only).</summary>
    Sync = 3,

    /// <summary>Transport links.</summary>
    Tunnel = 4,

    /// <summary>Diagnostics.</summary>
    Diagnostics = 5,

    /// <summary>Git branches (Source and Dest only).</summary>
    Branches = 6,

    /// <summary>Service logs.</summary>
    Logs = 7
}
