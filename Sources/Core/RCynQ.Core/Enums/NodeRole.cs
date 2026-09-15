using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the role a node plays in an RCynQ (SyncBridge) chain.
/// </summary>
/// <remarks>
/// One code base serves all roles; the role is selected by configuration.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<NodeRole>))]
public enum NodeRole
{
    /// <summary>
    /// The node owns a local working copy, exchanges live patches with <see cref="Dest"/>
    /// and receives Git branches only through <see cref="Dest"/>.
    /// </summary>
    [Description("Local working copy; exchanges live patches with Dest and fetches Git data through Dest.")]
    Source = 1,

    /// <summary>
    /// The node is a blind relay: it only opens transport channels and never builds or applies patches.
    /// </summary>
    [Description("Blind relay that only maintains transport links.")]
    Mediator = 2,

    /// <summary>
    /// The node has access to the closed Git repository, provides git-over-ssh to <see cref="Source"/>
    /// and exchanges live patches with it.
    /// </summary>
    [Description("Owns the closed Git repository and exchanges live patches with Source.")]
    Dest = 3
}
