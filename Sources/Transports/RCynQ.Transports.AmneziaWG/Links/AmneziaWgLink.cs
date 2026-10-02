using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Exceptions;
using RCynQ.Core.State;
using RCynQ.Localization;
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
    public const string CONFIG_PATH_SETTING = "configPath";

    /// <summary>
    /// The TCP connect timeout of a probe.
    /// </summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>The CLI adapter.</summary>
    private readonly IAmneziaWgCli _cli;

    /// <summary>The TCP probe.</summary>
    private readonly ITcpProbe _probe;

    /// <summary>The localizer of the messages.</summary>
    private readonly ILocalizer _localizer;

    /// <summary>
    /// Initializes a new instance of the <see cref="AmneziaWgLink"/> class.
    /// </summary>
    /// <param name="localizer">The localizer of the messages.</param>
    /// <param name="config">The link configuration.</param>
    /// <param name="cli">The CLI adapter.</param>
    /// <param name="probe">The TCP probe.</param>
    /// <param name="timeProvider">The clock.</param>
    public AmneziaWgLink(ILocalizer localizer, ChainLinkConfig config, IAmneziaWgCli cli, ITcpProbe probe, TimeProvider timeProvider)
        : base(localizer, config, TransportKind.AmneziaWG, timeProvider)
    {
        _localizer = localizer ?? throw new ArgumentNullException(nameof(localizer));
        _cli = cli;
        _probe = probe;
    }

    /// <summary>
    /// Gets the configured configuration path.
    /// </summary>
    /// <exception cref="SyncBridgeException">The setting is missing.</exception>
    private string ConfigPath => Config.TransportSettings.TryGetValue(CONFIG_PATH_SETTING, out var path) && !string.IsNullOrWhiteSpace(path)
        ? path
        : throw new SyncBridgeException(_localizer.Format("Awg.NeedsConfig", Config.Name, CONFIG_PATH_SETTING));

    /// <inheritdoc />
    protected override async Task StartCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _cli.StartAsync(ConfigPath, cancellationToken);
        }
        catch (ArgumentException ex)
        {
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
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
            throw new SyncBridgeException(_localizer.Describe(ex), ex);
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
            return CreateState(LinkStatus.Failed, _localizer.Describe(ex));
        }

        if (!status.Running)
        {
            return CreateState(LinkStatus.Failed, _localizer.Format("Awg.Down", status.InterfaceName, status.Message));
        }

        // Like the direct link: a peer without an SSH server is checked on its own TLS port, where it does listen.
        var peer = Config.Peer;
        var port = peer.ReachablePort;
        return await _probe.CanConnectAsync(peer.Host, port, ProbeTimeout, cancellationToken)
            ? CreateState(LinkStatus.Ready, _localizer.Format("Awg.Up", status.InterfaceName, status.LocalIp ?? _localizer.Get("Awg.NoAddress"), peer.Host, port))
            : CreateState(LinkStatus.Failed, _localizer.Format("Awg.UpUnreachable", status.InterfaceName, peer.Host, port));
    }

    /// <inheritdoc />
    protected override async Task<DiagnosticResult> DiagnoseCoreAsync(CancellationToken cancellationToken)
    {
        if (!Config.TransportSettings.TryGetValue(CONFIG_PATH_SETTING, out var path) || !File.Exists(path))
        {
            return DiagnosticResult.Fail(CheckName, _localizer.Format("Awg.ConfigMissing", path),
                _localizer.Format("Awg.ConfigMissingFix", CONFIG_PATH_SETTING, Config.Name));
        }

        var state = await ProbeCoreAsync(cancellationToken);
        return state.IsUsable
            ? DiagnosticResult.Pass(CheckName, state.Message)
            : DiagnosticResult.Fail(
                CheckName,
                state.Message ?? _localizer.Get("Awg.TunnelNotUsable"),
                _localizer.Get("Awg.InstallFix"),
                new DiagnosticFix(DiagnosticFixKind.StartTransport, _localizer.Format("Awg.StartFix", Config.Name), new Dictionary<string, string> { ["linkId"] = Config.Id.ToString() }));
    }
}
