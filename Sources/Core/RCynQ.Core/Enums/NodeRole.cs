using System.ComponentModel;
using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// Defines the role a node plays in an RCynQ (SyncBridge) chain.
/// </summary>
/// <remarks>
/// One code base serves all roles; the role is selected by configuration.
/// </remarks>
[JsonConverter(typeof(NodeRoleJsonConverter))]
public enum NodeRole
{
    /// <summary>
    /// The node owns a local working copy, exchanges live patches with <see cref="Origin"/>
    /// and receives Git branches only through <see cref="Origin"/>.
    /// </summary>
    [Description("Local working copy; exchanges live patches with Origin and fetches Git data through Origin.")]
    Workstation = 1,

    /// <summary>
    /// The node is a blind relay: it only opens transport channels and never builds or applies patches.
    /// </summary>
    [Description("Blind relay that only maintains transport links.")]
    Relay = 2,

    /// <summary>
    /// The node keeps the closed Git repositories, offers them to <see cref="Workstation"/> as bundles it only reads them for
    /// and exchanges live patches with it.
    /// </summary>
    [Description("Keeps the closed Git repositories, offers them to Workstation and exchanges live patches with it.")]
    Origin = 3
}
