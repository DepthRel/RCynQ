using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;
using RCynQ.Transports.Abstractions.Links;

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
        var peer = Config.Peer;
        return await _probe.CanConnectAsync(peer.Host, peer.SshPort, ProbeTimeout, cancellationToken)
            ? CreateState(LinkStatus.Ready, $"{peer.Host}:{peer.SshPort} is reachable.")
            : CreateState(LinkStatus.Failed, $"{peer.Host}:{peer.SshPort} is not reachable.");
    }

    /// <inheritdoc />
    protected override async Task<DiagnosticResult> DiagnoseCoreAsync(CancellationToken cancellationToken)
    {
        var state = await ProbeCoreAsync(cancellationToken);
        return state.IsUsable
            ? DiagnosticResult.Pass(CheckName, state.Message)
            : DiagnosticResult.Fail(CheckName, state.Message ?? "Peer is not reachable.",
                $"Check that {Config.Peer.Host} resolves, that sshd listens on port {Config.Peer.SshPort} and that no firewall blocks it, or configure a tunnel transport for this link.");
    }
}
