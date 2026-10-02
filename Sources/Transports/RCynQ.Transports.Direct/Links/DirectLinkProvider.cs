using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;
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

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectLinkProvider"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public DirectLinkProvider(ILocalizer localizer, ITcpProbe probe, TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _probe = probe;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public TransportKind Kind => TransportKind.Direct;

    /// <inheritdoc />
    public ITransportLink Create(ChainLinkConfig config) => new DirectLink(_localizer, config, _probe, _timeProvider);
}
