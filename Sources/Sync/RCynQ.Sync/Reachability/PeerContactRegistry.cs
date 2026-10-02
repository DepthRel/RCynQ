using System.Collections.Concurrent;
using RCynQ.Core.Ids;

namespace RCynQ.Sync.Reachability;

/// <summary>
/// Remembers when each peer last reached this node with a request this node accepted.
/// </summary>
/// <remarks>
/// The path from a peer to this node is not the path from this node to the peer: each direction passes the mediator
/// with its own key. What a peer managed is therefore worth telling next to what this node managed. The contacts are
/// kept for as long as the service runs; they describe the present, not a history.
/// </remarks>
public sealed class PeerContactRegistry
{
    /// <summary>The last contact of each peer.</summary>
    private readonly ConcurrentDictionary<NodeId, DateTimeOffset> _contacts = new();

    /// <summary>
    /// Records that a peer reached this node.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <param name="at">The time of the contact.</param>
    public void Record(NodeId peer, DateTimeOffset at)
        => _contacts.AddOrUpdate(peer, at, (_, known) => at > known ? at : known);

    /// <summary>
    /// Gets the time a peer last reached this node.
    /// </summary>
    /// <param name="peer">The peer.</param>
    /// <returns>The time, or <see langword="null"/> when the peer has not reached this node since the service started.</returns>
    public DateTimeOffset? LastHeard(NodeId peer)
        => _contacts.TryGetValue(peer, out var at) ? at : null;
}
