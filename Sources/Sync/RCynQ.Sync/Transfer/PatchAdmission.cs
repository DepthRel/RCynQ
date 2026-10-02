using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Security;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.Patches;
using RCynQ.Localization;
using RCynQ.Sync.Security;
using RCynQ.Sync.Signing;

namespace RCynQ.Sync.Transfer;

/// <summary>
/// Decides whether a request of the patch channel is let in: who sends it, for whom it is, and whether it is signed.
/// </summary>
/// <remarks>
/// Every check happens before anything is stored or written. A new manifest is checked in full
/// (<see cref="ValidateAsync"/>): role, link token (constant time), sender header, receiver, known counterpart peer
/// with a signing key and the signature of the manifest. A request that belongs to an open session
/// (<see cref="AuthorizeSessionAsync"/>) is checked against the patch the session stored.
/// </remarks>
public sealed class PatchAdmission
{
    /// <summary>The local node.</summary>
    private readonly NodeConfig _node;

    /// <summary>The peer repository.</summary>
    private readonly IPeerRepository _peers;

    /// <summary>The signature service.</summary>
    private readonly ISignatureService _signatures;

    /// <summary>The link token provider.</summary>
    private readonly LinkTokenProvider _tokens;

    /// <summary>The patch repository.</summary>
    private readonly IPatchRepository _patches;

    /// <summary>Tells which repository the node works with now, or <see langword="null"/> when no repository is checked.</summary>
    private readonly IActiveRepository? _repository;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="PatchAdmission"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="node">The local node.</param>
    /// <param name="peers">The peer repository.</param>
    /// <param name="signatures">The signature service.</param>
    /// <param name="tokens">The link token provider.</param>
    /// <param name="patches">The patch repository.</param>
    /// <param name="repository">Tells which repository the node works with now; <see langword="null"/> checks no repository.</param>
    public PatchAdmission(ILocalizer localizer, NodeConfig node, IPeerRepository peers, ISignatureService signatures, LinkTokenProvider tokens, IPatchRepository patches, IActiveRepository? repository = null)
    {
        _repository = repository;
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _node = node;
        _peers = peers;
        _signatures = signatures;
        _tokens = tokens;
        _patches = patches;
    }

    /// <summary>
    /// Validates a manifest before anything is stored.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is valid.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    public async Task ValidateAsync(PatchEnvelope envelope, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAuthorizedAsync(context, cancellationToken);
        if (!NodeId.TryParse(context.SenderNodeId, out var headerSender) || headerSender != envelope.SourceNode)
        {
            throw new PatchRejectedException(PatchRejectionReason.SenderMismatch, _localizer.Get("Receive.SenderMismatch"));
        }

        if (envelope.TargetNode != _node.Id)
        {
            throw new PatchRejectedException(PatchRejectionReason.WrongReceiver, _localizer.Format("Receive.WrongReceiver", envelope.TargetNode, _node.Id));
        }

        var peer = await GetCounterpartAsync(envelope.SourceNode, cancellationToken);
        if (!envelope.IsSigned ||
            !_signatures.Verify(PayloadCanonicalizer.Build(envelope), envelope.Signature, Convert.FromBase64String(peer.SigningPublicKey!)))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidSignature, _localizer.Get("Receive.InvalidSignature"));
        }

        // A patch changes the working tree, so it is taken only for the repository the node works with now. A node that
        // does not know the name of its repository yet (before its first contact) has nothing to compare with.
        if (_repository?.Current is { IsBound: true } active && !active.IsNamed(envelope.Repository))
        {
            throw new PatchRejectedException(
                PatchRejectionReason.RepositoryMismatch,
                _localizer.Format("Receive.RepositoryMismatch", _node.Name, active.Name, envelope.Repository ?? "?"));
        }
    }

    /// <summary>
    /// Checks a request of the counterpart endpoint that carries no patch: the role of this node, the link token, and
    /// that the sender is the peer this node would accept a patch from.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The peer that sent the request.</returns>
    /// <exception cref="PatchRejectedException">The request is refused.</exception>
    public async Task<PeerDescriptor> AuthorizePeerAsync(PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAuthorizedAsync(context, cancellationToken);
        if (!NodeId.TryParse(context.SenderNodeId, out var sender))
        {
            throw new PatchRejectedException(PatchRejectionReason.UnknownSender, _localizer.Format("Receive.UnknownSender", context.SenderNodeId ?? string.Empty, CounterpartRole));
        }

        return await GetCounterpartAsync(sender, cancellationToken);
    }

    /// <summary>
    /// Checks a request that belongs to an open session: the token, the sender and the session itself.
    /// </summary>
    /// <param name="patchId">The patch identifier.</param>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The stored patch of the session.</returns>
    /// <exception cref="PatchRejectedException">The request is not allowed or the session is unknown.</exception>
    public async Task<PatchMetadata> AuthorizeSessionAsync(Guid patchId, PatchReceiveContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        await EnsureAuthorizedAsync(context, cancellationToken);
        var metadata = await _patches.GetAsync(patchId, cancellationToken)
            ?? throw new PatchRejectedException(PatchRejectionReason.Malformed, _localizer.Format("Receive.UnknownSession", patchId));
        if (metadata.Direction != PatchDirection.Incoming)
        {
            throw new PatchRejectedException(PatchRejectionReason.Malformed, _localizer.Format("Receive.UnknownSession", patchId));
        }

        if (!NodeId.TryParse(context.SenderNodeId, out var sender) || sender != metadata.SourceNode)
        {
            throw new PatchRejectedException(PatchRejectionReason.SenderMismatch, _localizer.Get("Receive.SenderMismatch"));
        }

        if (metadata.Status != PatchStatus.Pending)
        {
            throw new PatchRejectedException(PatchRejectionReason.Replay, _localizer.Format("Replay.Again", patchId));
        }

        return metadata;
    }

    /// <summary>
    /// Gets the role of the endpoint this node exchanges patches with.
    /// </summary>
    private NodeRole CounterpartRole => _node.Role == NodeRole.Workstation ? NodeRole.Origin : NodeRole.Workstation;

    /// <summary>
    /// Finds the counterpart endpoint a request names: a known peer of the opposite role with a signing key.
    /// </summary>
    /// <param name="sender">The identifier of the sender.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The peer.</returns>
    /// <exception cref="PatchRejectedException">The sender is not such a peer.</exception>
    private async Task<PeerDescriptor> GetCounterpartAsync(NodeId sender, CancellationToken cancellationToken)
    {
        var peer = await _peers.GetAsync(sender, cancellationToken);
        if (peer?.SigningPublicKey is null || peer.Role != CounterpartRole)
        {
            throw new PatchRejectedException(PatchRejectionReason.UnknownSender, _localizer.Format("Receive.UnknownSender", sender, CounterpartRole));
        }

        return peer;
    }

    /// <summary>
    /// Checks the role and the link token of a request.
    /// </summary>
    /// <param name="context">The request context.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A task that completes when the request is allowed.</returns>
    /// <exception cref="PatchRejectedException">The node does not accept patches or the token is wrong.</exception>
    private async Task EnsureAuthorizedAsync(PatchReceiveContext context, CancellationToken cancellationToken)
    {
        if (!_node.ParticipatesInPatchChannel)
        {
            throw new PatchRejectedException(PatchRejectionReason.RoleNotAllowed, _localizer.Format("Receive.RoleNotAllowed", _node.Role));
        }

        if (!LinkTokenProvider.Matches(await _tokens.GetTokenAsync(cancellationToken), context.LinkToken))
        {
            throw new PatchRejectedException(PatchRejectionReason.InvalidToken, _localizer.Get("Receive.InvalidToken"));
        }
    }
}
