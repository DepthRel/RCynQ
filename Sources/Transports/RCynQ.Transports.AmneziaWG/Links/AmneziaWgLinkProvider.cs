using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AmneziaWgLinkProvider"/> class.
    /// </summary>
    /// <param name="cli">The CLI adapter.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public AmneziaWgLinkProvider(IAmneziaWgCli cli, ITcpProbe probe, TimeProvider timeProvider)
    {
        _cli = cli;
        _probe = probe;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public TransportKind Kind => TransportKind.AmneziaWG;

    /// <inheritdoc />
    public ITransportLink Create(ChainLinkConfig config) => new AmneziaWgLink(config, _cli, _probe, _timeProvider);
}
