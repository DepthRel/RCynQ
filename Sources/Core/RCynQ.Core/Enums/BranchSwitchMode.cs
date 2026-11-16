using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// What happens to uncommitted changes of the working tree when the Workstation switches to another branch.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BranchSwitchMode>))]
public enum BranchSwitchMode
{
    /// <summary>
    /// Switch only when the working tree has no changes; otherwise answer with the changed files and change nothing.
    /// </summary>
    [Description("Switch only a clean working tree.")]
    IfClean = 1,

    /// <summary>
    /// Carry the changes over to the new branch, as a plain <c>git switch</c> does; Git refuses when it cannot.
    /// </summary>
    [Description("Carry the changes over to the new branch.")]
    CarryChanges = 2,

    /// <summary>
    /// Take the changes of a patch the user has already saved out of the working tree, then switch.
    /// </summary>
    [Description("Take the saved changes out, then switch.")]
    SavePatch = 3
}
