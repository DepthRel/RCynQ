using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines what a rollback of commits does with the changes of the commits it drops, as <c>git reset</c> does.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CommitResetMode>))]
public enum CommitResetMode
{
    /// <summary>
    /// The changes stay in the index, ready to be committed again (<c>git reset --soft</c>).
    /// </summary>
    [Description("Keep the changes staged.")]
    Soft = 1,

    /// <summary>
    /// The changes stay in the working tree, not staged (<c>git reset --mixed</c>).
    /// </summary>
    [Description("Keep the changes in the working tree, not staged.")]
    Mixed = 2,

    /// <summary>
    /// The changes are discarded (<c>git reset --hard</c>).
    /// </summary>
    [Description("Discard the changes.")]
    Hard = 3
}
