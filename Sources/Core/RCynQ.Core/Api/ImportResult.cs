using RCynQ.Core.Enums;
using RCynQ.Core.Ids;

namespace RCynQ.Core.Api;

/// <summary>
/// Result of importing a peer configuration.
/// </summary>
/// <param name="PeerId">The imported peer.</param>
/// <param name="PeerRole">The role of the peer.</param>
/// <param name="LinkCreated">Whether a new chain link was created.</param>
/// <param name="LinkId">The created or updated link, or <see langword="null"/>.</param>
/// <param name="Message">A human-readable summary.</param>
public sealed record ImportResult(NodeId PeerId, NodeRole PeerRole, bool LinkCreated, Guid? LinkId, string Message)
{
    /// <summary>
    /// Gets the decision the import waits for, or <see langword="null"/> when the import is done.
    /// </summary>
    /// <remarks>
    /// When set, nothing was stored: the caller repeats the import with one of the offered decisions.
    /// </remarks>
    public ImportDecisionRequest? Decision { get; init; }

    /// <summary>
    /// Gets a value indicating whether the import changed nothing and waits for a decision.
    /// </summary>
    public bool NeedsDecision => Decision is not null;
}
