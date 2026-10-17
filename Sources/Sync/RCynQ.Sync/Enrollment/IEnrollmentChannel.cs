using RCynQ.Core.Api;
using RCynQ.Core.Config;

namespace RCynQ.Sync.Enrollment;

/// <summary>
/// Delivers an enrollment to the node at the end of a chain and brings its answer back.
/// </summary>
public interface IEnrollmentChannel
{
    /// <summary>
    /// Sends the enrollment.
    /// </summary>
    /// <param name="chain">The links to the node and to the Relays on the way; the node is the last one by priority.</param>
    /// <param name="ticket">The ticket, whose one-time key logs in to an SSH server.</param>
    /// <param name="request">The enrollment.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The answer of the node.</returns>
    /// <exception cref="Core.Exceptions.SyncBridgeException">The node could not be reached or answered nonsense.</exception>
    Task<EnrollmentResponse> SendAsync(IReadOnlyList<ChainLinkConfig> chain, InvitationTicket ticket, EnrollmentRequest request, CancellationToken cancellationToken = default);
}
