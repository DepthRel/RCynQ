using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// What a choice of the changes of a file came to.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartialChoiceOutcome>))]
public enum PartialChoiceOutcome
{
    /// <summary>Every change was chosen: the whole file goes.</summary>
    Whole = 1,

    /// <summary>A part of the changes was chosen: only that part goes.</summary>
    Partial = 2,

    /// <summary>No change was chosen: the file has nothing to send.</summary>
    Nothing = 3
}
