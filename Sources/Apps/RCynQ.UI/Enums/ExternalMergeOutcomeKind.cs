namespace RCynQ.UI.Enums;

/// <summary>
/// How a merge in a merge tool ended.
/// </summary>
public enum ExternalMergeOutcomeKind
{
    /// <summary>The service applied the merge; the result says how.</summary>
    Completed,

    /// <summary>Some files no merge tool can merge; nothing was opened.</summary>
    Unmergeable,

    /// <summary>The user gave the merge up; the patch keeps waiting for a decision.</summary>
    Cancelled,

    /// <summary>The merge could not be applied; the message says why.</summary>
    Failed
}
