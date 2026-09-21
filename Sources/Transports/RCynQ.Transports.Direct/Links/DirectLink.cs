using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Localization;

namespace RCynQ.Transports.Direct.Links;

/// <summary>
/// A link over the existing network: nothing is started, readiness is TCP reachability of the peer's SSH port.
/// </summary>
public sealed class DirectLink : TransportLinkBase
{
    /// <summary>
    /// The TCP connect timeout of a probe.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// The TCP probe.
    /// </summary>
    private readonly ITcpProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="DirectLink"/> class.
    /// </summary>
    /// <param name="config">The link configuration.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public DirectLink(ChainLinkConfig config, ITcpProbe probe, TimeProvider timeProvider)
        : base(config, TransportKind.Direct, timeProvider)
    {
        _probe = probe;
    }

    /// <inheritdoc />
    protected override Task StartCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override Task StopCoreAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <inheritdoc />
    protected override async Task<LinkState> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        // A peer without an SSH server is checked on its own TLS port.
        var peer = Config.Peer;
        return await _probe.CanConnectAsync(peer.Host, peer.ReachablePort, ProbeTimeout, cancellationToken)
            ? CreateState(LinkStatus.Ready, Texts.Format("Direct.Reachable", peer.Host, peer.ReachablePort))
            : CreateState(LinkStatus.Failed, Texts.Format("Direct.NotReachable", peer.Host, peer.ReachablePort));
    }

    /// <inheritdoc />
    protected override async Task<DiagnosticResult> DiagnoseCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ProbeCoreAsync(cancellationToken);
        return state.IsUsable
            ? DiagnosticResult.Pass(CheckName, state.Message)
            : DiagnosticResult.Fail(CheckName, state.Message ?? Texts.Get("Direct.PeerNotReachable"),
                Config.Peer.EndpointMode == EndpointMode.Direct
                    ? Texts.Format("Direct.CheckOwnPort", Config.Peer.Host, Config.Peer.EndpointPort)
                    : Texts.Format("Direct.CheckSsh", Config.Peer.Host, Config.Peer.SshPort));
    }
}
