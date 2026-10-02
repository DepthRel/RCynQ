using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines where the sync base of a node comes from.
/// </summary>
/// <remarks>
/// The sync base is the state this node believes its peer has: the working tree is compared against it to find what to send,
/// and it decides whether an incoming change can be applied without a conflict.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<SyncBaseMode>))]
public enum SyncBaseMode
{
    /// <summary>
    /// There is no sync base yet; the node can neither send nor receive patches.
    /// </summary>
    [Description("No sync base yet.")]
    None = 0,

    /// <summary>
    /// The sync base is built automatically from a Git commit both nodes have: <c>HEAD</c> on Origin, the commit Workstation last
    /// pulled from Origin on Workstation.
    /// </summary>
    [Description("Built automatically from a Git commit both nodes have.")]
    Git = 1,

    /// <summary>
    /// The sync base is a snapshot the user took by hand, for a working tree that is not a Git repository.
    /// </summary>
    [Description("A snapshot taken by hand, for a working tree without Git.")]
    Manual = 2
}
