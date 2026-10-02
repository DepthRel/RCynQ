using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Localization;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.AmneziaWG.Cli;

namespace RCynQ.Transports.AmneziaWG.Links;

/// <summary>
/// <see cref="ITransportLinkProvider"/> for <see cref="AmneziaWgLink"/>.
/// </summary>
public sealed class AmneziaWgLinkProvider : ITransportLinkProvider
{
    /// <summary>The CLI adapter.</summary>
    private readonly IAmneziaWgCli _cli;

    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>The clock.</summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AmneziaWgLinkProvider"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="cli">The CLI adapter.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public AmneziaWgLinkProvider(ILocalizer localizer, IAmneziaWgCli cli, ITcpProbe probe, TimeProvider timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _cli = cli;
        _probe = probe;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public TransportKind Kind => TransportKind.AmneziaWG;

    /// <inheritdoc />
    public ITransportLink Create(ChainLinkConfig config) => new AmneziaWgLink(_localizer, config, _cli, _probe, _timeProvider);
}
