using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// The role of one line in the diff of a file.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiffLineKind>))]
public enum DiffLineKind
{
    /// <summary>A line both versions have, shown around a change.</summary>
    Context = 1,

    /// <summary>A line only the version of the other node has.</summary>
    Removed = 2,

    /// <summary>A line only the working tree has.</summary>
    Added = 3
}
