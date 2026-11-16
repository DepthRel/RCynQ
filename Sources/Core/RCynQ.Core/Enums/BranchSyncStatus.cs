using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Tells how a branch of the Workstation working copy stands to the branches fetched from the Origin.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<BranchSyncStatus>))]
public enum BranchSyncStatus
{
    /// <summary>
    /// The branch is on this computer and the Origin has a branch of the same name (<c>rcynq/&lt;name&gt;</c>).
    /// </summary>
    [Description("The branch is here and on the Origin.")]
    Pulled = 1,

    /// <summary>
    /// The Origin has the branch, this computer has no local branch of that name yet.
    /// </summary>
    [Description("The branch is on the Origin and not pulled yet.")]
    NotPulled = 2,

    /// <summary>
    /// The branch is only on this computer: the Origin has no branch of that name.
    /// </summary>
    [Description("The branch is only on this computer.")]
    LocalOnly = 3
}
