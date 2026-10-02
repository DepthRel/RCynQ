using RCynQ.Core.Config;
using RCynQ.Core.State;

namespace RCynQ.Core.Abstractions.Transports;

/// <summary>
/// Tells the state of the link to the counterpart endpoint of a chain the way a patch travels: through the mediators.
/// </summary>
/// <remarks>
/// A transport link checks whether its peer answers on the network when asked directly. That is true for a neighbour
/// and wrong for the counterpart endpoint behind a mediator, which cannot be reached directly by design: such a check
/// reports "no connection" for a node that exchanges patches without trouble. The state of that link comes from here
/// instead.
/// </remarks>
public interface IEndpointProbe
{
    /// <summary>
    /// Gets the state of a link when it leads to the counterpart endpoint of the chain.
    /// </summary>
    /// <param name="link">The link.</param>
    /// <param name="chain">All links of the node.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// The state, or <see langword="null"/> when the link does not lead to the counterpart endpoint and its transport
    /// is the one to ask.
    /// </returns>
    Task<LinkState?> GetStateAsync(ChainLinkConfig link, IReadOnlyList<ChainLinkConfig> chain, CancellationToken cancellationToken = default);
}
