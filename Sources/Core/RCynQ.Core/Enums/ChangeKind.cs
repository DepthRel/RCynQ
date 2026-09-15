using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the kind of change applied to a file between two states.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<ChangeKind>))]
public enum ChangeKind
{
    /// <summary>
    /// The file exists only in the incoming state.
    /// </summary>
    [Description("The file was added in the incoming state.")]
    Added = 1,

    /// <summary>
    /// The file exists in both states with different content.
    /// </summary>
    [Description("The file content was modified in the incoming state.")]
    Modified = 2,

    /// <summary>
    /// The file exists only in the base state.
    /// </summary>
    [Description("The file was removed in the incoming state.")]
    Deleted = 3,

    /// <summary>
    /// The file was moved to a new path. The directory manifest builder reports renames as
    /// an <see cref="Added"/> and <see cref="Deleted"/> pair; this member is reserved for producers
    /// that can detect renames explicitly.
    /// </summary>
    [Description("The file was renamed in the incoming state.")]
    Renamed = 4
}
