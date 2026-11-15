using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Whether only a part of the changes of a file goes to the other node.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PartialChangeState>))]
public enum PartialChangeState
{
    /// <summary>The whole file goes, as it is in the working tree.</summary>
    None = 0,

    /// <summary>Only the chosen part of the changes goes.</summary>
    Partial = 1,

    /// <summary>
    /// Only the chosen part goes, but the file was edited after the choice: the edits made since stay out until the user
    /// chooses again.
    /// </summary>
    Outdated = 2
}
