using RCynQ.Core.Enums;

namespace RCynQ.Core.Exceptions;

/// <summary>
/// Thrown when a received patch envelope is refused before it is stored.
/// </summary>
public sealed class PatchRejectedException : SyncBridgeException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PatchRejectedException"/> class.
    /// </summary>
    /// <param name="reason">The rejection reason.</param>
    /// <param name="message">The error message.</param>
    public PatchRejectedException(PatchRejectionReason reason, string message)
        : base(message)
    {
        Reason = reason;
    }

    /// <summary>
    /// Gets the rejection reason.
    /// </summary>
    public PatchRejectionReason Reason { get; }
}
