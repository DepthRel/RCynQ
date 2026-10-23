using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Tells whether the commits of the current branch of a Workstation can be listed and sent, and why not.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<UnsentCommitsState>))]
public enum UnsentCommitsState
{
    /// <summary>
    /// The branch is built right on the commit of the Origin; its unsent commits, if any, are listed.
    /// </summary>
    [Description("The unsent commits are listed.")]
    Ready = 1,

    /// <summary>
    /// The node is not a Workstation.
    /// </summary>
    [Description("Only a Workstation sends commits.")]
    NotWorkstation = 2,

    /// <summary>
    /// The working tree is not a Git repository.
    /// </summary>
    [Description("The working tree is not a Git repository.")]
    NoRepository = 3,

    /// <summary>
    /// No branch is checked out, or the repository has no commits.
    /// </summary>
    [Description("No branch is checked out.")]
    Detached = 4,

    /// <summary>
    /// The Origin has no branch of that name.
    /// </summary>
    [Description("The Origin has no such branch.")]
    NoOriginBranch = 5,

    /// <summary>
    /// The branch does not start right on the commit of the Origin.
    /// </summary>
    [Description("The branch diverged from the Origin.")]
    Diverged = 6,

    /// <summary>
    /// There are more unsent commits than one delivery carries.
    /// </summary>
    [Description("Too many unsent commits.")]
    TooMany = 7
}
