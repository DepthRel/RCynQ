using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// What taking back the changes of one file of the working tree came to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<WorkingTreeRevertOutcome>))]
public enum WorkingTreeRevertOutcome
{
    /// <summary>The file was restored from the Git index; its staged changes stay.</summary>
    Restored = 1,

    /// <summary>The file was untracked, so it was deleted.</summary>
    Deleted = 2,

    /// <summary>The file has no change outside the index: only staged changes, which stay.</summary>
    NothingToRevert = 3,

    /// <summary>Git neither has the path in the index nor lists it as untracked; nothing was touched.</summary>
    Unknown = 4
}
