using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;

namespace RCynQ.Transports.Abstractions.Links;

/// <summary>
/// Creates transport links of one <see cref="TransportKind"/>. Each transport package registers one provider.
/// </summary>
public interface ITransportLinkProvider
{
    /// <summary>
    /// Gets the transport kind produced by this provider.
    /// </summary>
    TransportKind Kind { get; }

    /// <summary>
    /// Creates a link for the configuration.
    /// </summary>
    /// <param name="config">The link configuration whose <see cref="ChainLinkConfig.Transport"/> equals <see cref="Kind"/>.</param>
    /// <returns>A new, not started link.</returns>
    ITransportLink Create(ChainLinkConfig config);
}
