using System.Text.Json.Serialization;

namespace RCynQ.Core.Enums;

/// <summary>
/// What to do with an imported peer configuration when the node already knows a peer of the same role.
/// </summary>
/// <remarks>
/// An import that meets no such peer needs no decision. Otherwise nothing is changed until one is given: the node does
/// not guess whether a second address of a node is another way to it or its new address.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<ImportDecision>))]
public enum ImportDecision
{
    /// <summary>
    /// The imported address is another way to a node that is already known: it is added to the routes of its link.
    /// </summary>
    AlternativeRoute = 1,

    /// <summary>
    /// The imported configuration takes the place of a known one: the new address of the same node, or another node in
    /// the place of the chosen link.
    /// </summary>
    Replace = 2,

    /// <summary>
    /// The imported node is added next to the known ones: one more Relay to pass on the way, or one more neighbour of a
    /// Relay.
    /// </summary>
    AddHop = 3
}
