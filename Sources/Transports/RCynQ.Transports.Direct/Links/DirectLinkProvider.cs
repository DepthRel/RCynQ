using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;
using RCynQ.Transports.Abstractions.Links;

namespace RCynQ.Transports.Direct.Links;

/// <summary>
/// <see cref="ITransportLinkProvider"/> for <see cref="DirectLink"/>.
/// </summary>
public sealed class DirectLinkProvider : ITransportLinkProvider
{
    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectLinkProvider"/> class.
    /// </summary>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public DirectLinkProvider(ITcpProbe probe, TimeProvider timeProvider)
    {
        _probe = probe;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public TransportKind Kind => TransportKind.Direct;

    /// <inheritdoc />
    public ITransportLink Create(ChainLinkConfig config) => new DirectLink(config, _probe, _timeProvider);
}
