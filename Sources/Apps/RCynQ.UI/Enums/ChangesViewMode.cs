namespace RCynQ.UI.Enums;

/// <summary>
/// Where the diff of a changed file is shown and a part of its changes is chosen.
/// </summary>
public enum ChangesViewMode
{
    /// <summary>In the window of RCynQ, with a check mark on every change.</summary>
    BuiltIn,

    /// <summary>In the merge tool of the settings: the right pane is edited.</summary>
    ExternalTool
}
