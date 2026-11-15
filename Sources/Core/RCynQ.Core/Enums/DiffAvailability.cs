using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Whether the diff of a file can be shown line by line, and why not.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<DiffAvailability>))]
public enum DiffAvailability
{
    /// <summary>Both versions are text: the diff is shown line by line.</summary>
    Text = 1,

    /// <summary>One of the versions is binary.</summary>
    Binary = 2,

    /// <summary>One of the versions is too large to be shown here.</summary>
    TooLarge = 3,

    /// <summary>One of the versions is a symbolic link.</summary>
    Link = 4,

    /// <summary>The other node already has this version of the file: there is nothing to show.</summary>
    Same = 5
}
