using RCynQ.Core.Enums;
using RCynQ.Core.Validation;

namespace RCynQ.Core.State;

/// <summary>
/// The runtime state of a transport link.
/// </summary>
public sealed record LinkState
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LinkState"/> record.
    /// </summary>
    /// <param name="linkId">The link identifier.</param>
    /// <param name="linkName">The link display name.</param>
    /// <param name="kind">The transport currently in use.</param>
    /// <param name="status">The link status.</param>
    /// <param name="message">An optional status message.</param>
    /// <param name="updatedAtUtc">The time the state was observed.</param>
    /// <exception cref="ArgumentException">An enum is not defined or the name is blank.</exception>
    public LinkState(Guid linkId, string linkName, TransportKind kind, LinkStatus status, string? message, DateTimeOffset? updatedAtUtc)
    {
        LinkId = linkId;
        LinkName = Guard.NotBlank(linkName, nameof(linkName));
        Kind = Guard.Defined(kind, nameof(kind));
        Status = Guard.Defined(status, nameof(status));
        Message = message;
        UpdatedAtUtc = updatedAtUtc?.ToUniversalTime();
    }

    /// <summary>
    /// Gets the link identifier.
    /// </summary>
    public Guid LinkId { get; }

    /// <summary>
    /// Gets the link display name.
    /// </summary>
    public string LinkName { get; }

    /// <summary>
    /// Gets the transport currently in use.
    /// </summary>
    public TransportKind Kind { get; }

    /// <summary>
    /// Gets the link status.
    /// </summary>
    public LinkStatus Status { get; }

    /// <summary>
    /// Gets an optional status message.
    /// </summary>
    public string? Message { get; }

    /// <summary>
    /// Gets the time the state was observed.
    /// </summary>
    public DateTimeOffset? UpdatedAtUtc { get; }

    /// <summary>
    /// Gets a value indicating whether the peer is reachable through the link.
    /// </summary>
    public bool IsUsable => Status is LinkStatus.Ready or LinkStatus.Degraded;
}
