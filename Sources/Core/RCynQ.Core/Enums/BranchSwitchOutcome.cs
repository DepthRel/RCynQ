using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// How a switch of the Workstation to another branch ended.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BranchSwitchOutcome>))]
public enum BranchSwitchOutcome
{
    /// <summary>
    /// The working tree is on the asked branch now (or already was).
    /// </summary>
    [Description("The branch was switched.")]
    Switched = 1,

    /// <summary>
    /// The working tree has changes and the request did not say what to do with them; nothing changed.
    /// </summary>
    [Description("The working tree has changes; the user decides what happens to them.")]
    ChangesInTheWay = 2,

    /// <summary>
    /// The switch was refused or failed; the working tree is as it was.
    /// </summary>
    [Description("The branch was not switched.")]
    Refused = 3
}
