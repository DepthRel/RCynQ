using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RCynQ.Core.Abstractions;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.AmneziaWG.Cli;

namespace RCynQ.Transports.AmneziaWG.Links;

/// <summary>
/// A link through an AmneziaWG tunnel. The tunnel is ready when its interface is up and the peer's SSH port is reachable.
/// </summary>
public sealed class AmneziaWgLink : TransportLinkBase
{
    /// <summary>
    /// The transport setting with the absolute <c>.conf</c> path.
    /// </summary>
    public const string ConfigPathSetting = "configPath";

    /// <summary>
    /// The TCP connect timeout of a probe.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The CLI adapter.</summary>
    private readonly IAmneziaWgCli _cli;

    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>
    /// Initializes a new instance of the <see cref="AmneziaWgLink"/> class.
    /// </summary>
    /// <param name="config">The link configuration.</param>
    /// <param name="cli">The CLI adapter.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public AmneziaWgLink(ChainLinkConfig config, IAmneziaWgCli cli, ITcpProbe probe, TimeProvider timeProvider)
        : base(config, TransportKind.AmneziaWG, timeProvider)
    {
        _cli = cli;
        _probe = probe;
    }

    /// <summary>
    /// Gets the configured configuration path.
    /// </summary>
    /// <exception cref="SyncBridgeException">The setting is missing.</exception>
    private string ConfigPath => Config.TransportSettings.TryGetValue(ConfigPathSetting, out var path) && !string.IsNullOrWhiteSpace(path)
        ? path
        : throw new SyncBridgeException($"Link '{Config.Name}' needs the transport setting '{ConfigPathSetting}' with the AmneziaWG .conf path.");

    /// <inheritdoc />
    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cli.StartAsync(ConfigPath, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    protected override async Task StopCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cli.StopAsync(ConfigPath, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(ex.Message, ex);
        }
    }

    /// <inheritdoc />
    protected override async Task<LinkState> ProbeCoreAsync(CancellationToken cancellationToken)
    {
        AmneziaWgStatus status;
        try
        {
            status = await _cli.GetStatusAsync(ConfigPath, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            return CreateState(LinkStatus.Failed, ex.Message);
        }

        if (!status.Running)
        {
            return CreateState(LinkStatus.Failed, $"Interface {status.InterfaceName} is down: {status.Message}");
        }

        var peer = Config.Peer;
        return await _probe.CanConnectAsync(peer.Host, peer.SshPort, ProbeTimeout, cancellationToken)
            ? CreateState(LinkStatus.Ready, $"Interface {status.InterfaceName} ({status.LocalIp ?? "no address"}) is up; {peer.Host}:{peer.SshPort} is reachable.")
            : CreateState(LinkStatus.Failed, $"Interface {status.InterfaceName} is up, but {peer.Host}:{peer.SshPort} is not reachable through it.");
    }

    /// <inheritdoc />
    protected override async Task<DiagnosticResult> DiagnoseCoreAsync(CancellationToken cancellationToken)
    {
        if (!Config.TransportSettings.TryGetValue(ConfigPathSetting, out var path) || !File.Exists(path))
        {
            return DiagnosticResult.Fail(CheckName, $"AmneziaWG configuration '{path}' is missing.",
                $"Set the transport setting '{ConfigPathSetting}' of link '{Config.Name}' to an existing .conf file.");
        }

        var state = await ProbeCoreAsync(cancellationToken);
        return state.IsUsable
            ? DiagnosticResult.Pass(CheckName, state.Message)
            : DiagnosticResult.Fail(
                CheckName,
                state.Message ?? "Tunnel is not usable.",
                "Install AmneziaWG, make sure awg-quick (or amneziawg.exe) can run with the required privileges, and check the peer configuration.",
                new DiagnosticFix(DiagnosticFixKind.StartTransport, $"Start transport link '{Config.Name}'.", new Dictionary<string, string> { ["linkId"] = Config.Id.ToString() }));
    }
}
