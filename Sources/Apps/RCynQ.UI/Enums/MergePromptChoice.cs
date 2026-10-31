namespace RCynQ.UI.Enums;

/// <summary>
/// What the user answers when a merge in a merge tool needs a decision.
/// </summary>
public enum MergePromptChoice
{
    /// <summary>Look at the result again: the user saved it after the tool returned.</summary>
    CheckAgain,

    /// <summary>Open the file in the tool once more.</summary>
    OpenAgain,

    /// <summary>Take the result with the conflict markers left in it.</summary>
    KeepMarkers,

    /// <summary>Try the failed step again.</summary>
    Retry,

    /// <summary>Give the whole merge up; the patch keeps waiting for a decision.</summary>
    Cancel
}
