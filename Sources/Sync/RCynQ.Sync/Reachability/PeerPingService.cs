using RCynQ.Core.Api;
using RCynQ.Core.Config;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Patches;
using RCynQ.Sync.Transfer;

namespace RCynQ.Sync.Reachability;

/// <summary>
/// Answers the ping of the counterpart endpoint: "are you there, and would you take a patch from me?".
/// </summary>
/// <remarks>
/// The answer is given only to the peer a patch would be accepted from, checked the same way a patch is: the role of
/// this node, the link token, and the sender being the known counterpart endpoint with a signing key. A positive
/// answer therefore means more than "the port is open": the next patch of that peer passes the same door.
/// </remarks>
public sealed class PeerPingService
{
    /// <summary>Decides whether a request is let in.</summary>
    private readonly PatchAdmission _admission;

    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The contacts the node has had from its peers.</summary>
    private readonly PeerContactRegistry _contacts;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="PeerPingService"/> class.
    /// </summary>
    /// <param name="admission">Decides whether a request is let in.</param>
    /// <param name="node">The local node.</param>
    /// <param name="contacts">The contacts the node has had from its peers.</param>
    /// <param name="timeProvider">The clock.</param>
    public PeerPingService(PatchAdmission admission, NodeConfig node, PeerContactRegistry contacts, TimeProvider timeProvider)
    {
        _admission = admission ?? throw new ArgumentNullException(nameof(admission));
        _node = node ?? throw new ArgumentNullException(nameof(node));
        _contacts = contacts ?? throw new ArgumentNullException(nameof(contacts));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    /// <summary>
    /// Answers a ping and remembers that the peer reached this node.
    /// </summary>
    /// <param name="context">The request context: who asks, and with which link token.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer: the identity of this node and the time.</returns>
    /// <exception cref="PatchRejectedException">The request is refused; a patch of the same sender would be refused too.</exception>
    public async Task<PeerPingResponse> AnswerAsync(PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        var peer = await _admission.AuthorizePeerAsync(context, cancellationToken);
        var now = _timeProvider.GetUtcNow();
        _contacts.Record(peer.Id, now);
        return new PeerPingResponse(_node.Id.Value, _node.Role, now);
    }
}
