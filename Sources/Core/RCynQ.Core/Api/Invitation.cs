namespace RCynQ.Core.Api;

/// <summary>
/// An invitation to join a node: everything the invited node needs to reach the inviting one and enroll there, passed
/// as one line of text. It is a secret, single-use and expires.
/// </summary>
/// <param name="Version">The format version; see <see cref="CURRENT_VERSION"/>.</param>
/// <param name="ProtocolVersion">The <see cref="ExportedNodeConfiguration.CURRENT_PROTOCOL_VERSION"/> of the inviting node.</param>
/// <param name="Inviter">The card of the inviting node; its host key or certificate fingerprint is pinned by the invited node.</param>
/// <param name="Path">The cards of the Relays the invited node goes through to reach the inviting one; empty for a direct link.</param>
/// <param name="Ticket">The ticket of the inviting node.</param>
/// <param name="RelayTicket">
/// The ticket of the Relay of <paramref name="Path"/> for the invited node, when the inviting node holds one; without it
/// the Relay must know the invited node already.
/// </param>
/// <param name="LinkToken">The shared link token, in an invitation from a Workstation or an Origin to its counterpart.</param>
public sealed record Invitation(
    int Version,
    int ProtocolVersion,
    ExportedNodeConfiguration Inviter,
    IReadOnlyList<ExportedNodeConfiguration> Path,
    InvitationTicket Ticket,
    InvitationTicket? RelayTicket = null,
    string? LinkToken = null)
{
    /// <summary>
    /// The current format version.
    /// </summary>
    public const int CURRENT_VERSION = 1;

    /// <summary>
    /// The text an invitation starts with.
    /// </summary>
    public const string PREFIX = "rcynq-invite:";
}
