namespace RCynQ.Sync.Enums;

/// <summary>
/// What a three-way merge does with one file of an incoming patch.
/// </summary>
public enum MergePlanKind
{
    /// <summary>Both sides have the same content already.</summary>
    Unchanged,

    /// <summary>The file was not changed locally; the incoming change is taken as it is.</summary>
    TakeIncoming,

    /// <summary>Both sides changed the file and the changes were combined.</summary>
    Merged,

    /// <summary>Both sides changed the same lines; the merged file holds conflict markers.</summary>
    MergedWithMarkers,

    /// <summary>Git cannot merge the file: a binary file, a link, or a file one side deleted and the other changed.</summary>
    CannotMerge
}
