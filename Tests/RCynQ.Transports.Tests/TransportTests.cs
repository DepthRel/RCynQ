using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using RCynQ.Core.Abstractions.Events;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Abstractions.Repositories;
using RCynQ.Core.Abstractions.Transports;
using RCynQ.Core.Config;
using RCynQ.Core.Diagnostics;
using RCynQ.Core.Enums;
using RCynQ.Core.Events;
using RCynQ.Core.Exceptions;
using RCynQ.Core.Ids;
using RCynQ.Core.State;
using RCynQ.Transports.Abstractions.Factory;
using RCynQ.Transports.Abstractions.Links;
using RCynQ.Transports.Abstractions.Management;
using RCynQ.Transports.AmneziaWG.Cli;
using RCynQ.Transports.AmneziaWG.Links;
using RCynQ.Transports.Direct.Links;

namespace RCynQ.Transports.Tests;

/// <summary>
/// Tests for Direct, AmneziaWG, the link factory and failover.
/// </summary>
public sealed class TransportTests : IDisposable
{
    /// <summary>A temporary AmneziaWG configuration file.</summary>
    private readonly string _config;

    /// <summary>The TCP probe mock.</summary>
    private readonly ITcpProbe _probe = Substitute.For<ITcpProbe>();

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportTests"/> class.
    /// </summary>
    public TransportTests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "rcynq-transport-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        _config = Path.Combine(directory, "awg0.conf");
        File.WriteAllText(_config, "[Interface]\nPrivateKey = x\nAddress = 10.8.0.2/32\n\n[Peer]\nEndpoint = relay.example:51820\n");
    }

    /// <summary>
    /// Verifies Direct success.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Direct_Is_Ready_When_Reachable()
    {
        _probe.CanConnectAsync("dest.internal", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        var link = new DirectLink(TestTexts.English, Link(TransportKind.Direct), _probe, TimeProvider.System);

        await link.StartAsync();

        link.CurrentState.Status.Should().Be(LinkStatus.Ready);
        (await link.GetStatusAsync()).IsUsable.Should().BeTrue();
        (await link.DiagnoseAsync()).Passed.Should().BeTrue();
        await link.StopAsync();
        link.CurrentState.Status.Should().Be(LinkStatus.Stopped);
    }

    /// <summary>
    /// Verifies Direct failure.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Direct_Fails_When_Unreachable()
    {
        _probe.CanConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        var link = new DirectLink(TestTexts.English, Link(TransportKind.Direct), _probe, TimeProvider.System);

        var start = () => link.StartAsync();

        (await start.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("not reachable");
        link.CurrentState.Status.Should().Be(LinkStatus.Failed);
        var diagnosis = await link.DiagnoseAsync();
        diagnosis.Passed.Should().BeFalse();
        diagnosis.SuggestedFix.Should().Contain("sshd");
    }

    /// <summary>
    /// Verifies that a peer without an SSH server is checked on its TLS port, with an advice that does not mention sshd.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Direct_Checks_The_Tls_Port_Of_A_Peer_Without_Ssh()
    {
        var peer = new PeerDescriptor(
            new NodeId("dest"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq",
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('B', 64), endpointPort: 18444);
        var config = new ChainLinkConfig(Guid.NewGuid(), "to-dest", TransportKind.Direct, peer, 0, 18444, 0);
        _probe.CanConnectAsync("dest.internal", 18444, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        var link = new DirectLink(TestTexts.English, config, _probe, TimeProvider.System);
        await link.StartAsync();

        link.CurrentState.Status.Should().Be(LinkStatus.Ready);
        await _probe.DidNotReceive().CanConnectAsync("dest.internal", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        _probe.CanConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        var diagnosis = await link.DiagnoseAsync();
        diagnosis.SuggestedFix.Should().Contain("18444").And.NotContain("sshd");
    }

    /// <summary>
    /// Verifies the AmneziaWG CLI commands and status parsing on Linux-style tools.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AmneziaWgCli_Uses_AwgQuick_And_Parses_Status()
    {
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Any<ProcessSpec>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var spec = call.Arg<ProcessSpec>();
            return spec.FileName == "awg"
                ? new ProcessResult(0, "interface: awg0\n  public key: abc\n", "")
                : new ProcessResult(0, "", "");
        });
        var cli = new ProcessAmneziaWgCli(TestTexts.English, processes, new AmneziaWgOptions { UseWindowsTunnelService = false });

        var status = await cli.StartAsync(_config);
        await cli.StopAsync(_config);

        status.Should().Be(new AmneziaWgStatus(true, "awg0", "10.8.0.2", null));
        await processes.Received(1).RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "awg-quick" && spec.Arguments.SequenceEqual(new[] { "up", _config })), Arg.Any<CancellationToken>());
        await processes.Received(1).RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "awg-quick" && spec.Arguments.SequenceEqual(new[] { "down", _config })), Arg.Any<CancellationToken>());
        await processes.Received().RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "awg" && spec.Arguments.SequenceEqual(new[] { "show", "awg0" })), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies the Windows tunnel service commands, failures and path validation.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AmneziaWgCli_Windows_Commands_And_Validation()
    {
        var processes = Substitute.For<IProcessRunner>();
        processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "amneziawg.exe"), Arg.Any<CancellationToken>()).Returns(new ProcessResult(1, "", "access denied"));
        processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "awg"), Arg.Any<CancellationToken>()).ThrowsAsync(new SyncBridgeException("could not be started"));
        var cli = new ProcessAmneziaWgCli(TestTexts.English, processes, new AmneziaWgOptions { UseWindowsTunnelService = true });

        var start = () => cli.StartAsync(_config);
        (await start.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("access denied");
        await processes.Received(1).RunAsync(Arg.Is<ProcessSpec>(spec => spec.Arguments.SequenceEqual(new[] { "/installtunnelservice", _config })), Arg.Any<CancellationToken>());

        var status = await cli.GetStatusAsync(_config);
        status.Running.Should().BeFalse();
        status.Message.Should().Contain("not available");

        var relative = () => cli.StartAsync("awg0.conf");
        var tooLong = () => ProcessAmneziaWgCli.GetInterfaceName("/etc/amnezia/this-name-is-too-long.conf");
        var injected = () => ProcessAmneziaWgCli.GetInterfaceName("/etc/amnezia/a;b.conf");
        await relative.Should().ThrowAsync<ArgumentException>();
        tooLong.Should().Throw<ArgumentException>();
        injected.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// Verifies the AmneziaWG link with a mocked CLI.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AmneziaWgLink_Starts_Checks_And_Stops()
    {
        var cli = Substitute.For<IAmneziaWgCli>();
        cli.GetStatusAsync(_config, Arg.Any<CancellationToken>()).Returns(new AmneziaWgStatus(true, "awg0", "10.8.0.2", null));
        _probe.CanConnectAsync("dest.internal", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        var link = new AmneziaWgLink(TestTexts.English, Link(TransportKind.AmneziaWG, _config), cli, _probe, TimeProvider.System);

        await link.StartAsync();
        link.CurrentState.Status.Should().Be(LinkStatus.Ready);
        await link.StopAsync();

        await cli.Received(1).StartAsync(_config, Arg.Any<CancellationToken>());
        await cli.Received(1).StopAsync(_config, Arg.Any<CancellationToken>());

        var missingSetting = new AmneziaWgLink(TestTexts.English, Link(TransportKind.AmneziaWG), cli, _probe, TimeProvider.System);
        var start = () => missingSetting.StartAsync();
        (await start.Should().ThrowAsync<SyncBridgeException>()).Which.Message.Should().Contain("configPath");

        cli.GetStatusAsync(_config, Arg.Any<CancellationToken>()).Returns(new AmneziaWgStatus(false, "awg0", null, "down"));
        var diagnosis = await link.DiagnoseAsync();
        diagnosis.Passed.Should().BeFalse();
        diagnosis.AutoFix!.Kind.Should().Be(DiagnosticFixKind.StartTransport);
    }

    /// <summary>
    /// Verifies provider selection and unsupported kinds.
    /// </summary>
    [Fact]
    public void Factory_Selects_Provider()
    {
        var factory = new TransportLinkFactory(TestTexts.English, new ITransportLinkProvider[]
        {
            new DirectLinkProvider(TestTexts.English, _probe, TimeProvider.System),
            new AmneziaWgLinkProvider(TestTexts.English, Substitute.For<IAmneziaWgCli>(), _probe, TimeProvider.System)
        });

        factory.Create(Link(TransportKind.Direct)).Should().BeOfType<DirectLink>();
        factory.Create(Link(TransportKind.AmneziaWG)).Should().BeOfType<AmneziaWgLink>();
        factory.IsSupported(TransportKind.Tailscale).Should().BeFalse();
        var unsupported = () => factory.Create(Link(TransportKind.WireGuard));
        unsupported.Should().Throw<ArgumentException>().WithMessage("*WireGuard*");
    }

    /// <summary>
    /// Verifies failover from a failing primary to a working fallback, and event publishing.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkManager_Fails_Over_And_Publishes()
    {
        var config = Link(TransportKind.AmneziaWG, _config, new[] { TransportKind.WireGuard, TransportKind.Direct });
        var (manager, events, _) = CreateManager(config, amneziaWorks: false, directWorks: true);

        var state = await manager.StartAsync(config.Id);

        state.Status.Should().Be(LinkStatus.Degraded);
        state.Kind.Should().Be(TransportKind.Direct);
        state.Message.Should().Contain("AmneziaWG").And.Contain("WireGuard: not available");
        await events.Received(1).PublishLinkStateAsync(Arg.Is<LinkStateChangedEvent>(evt => evt.State.Status == LinkStatus.Degraded), Arg.Any<CancellationToken>());

        (await manager.GetStatusAsync()).Should().ContainSingle().Which.Status.Should().Be(LinkStatus.Degraded);
        await events.Received(1).PublishLinkStateAsync(Arg.Any<LinkStateChangedEvent>(), Arg.Any<CancellationToken>());

        (await manager.StopAsync(config.Id)).Status.Should().Be(LinkStatus.Stopped);
        await events.Received(1).PublishLinkStateAsync(Arg.Is<LinkStateChangedEvent>(evt => evt.State.Status == LinkStatus.Stopped), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that a link fails when every transport fails, and that a usable primary is kept on failover.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkManager_Reports_Failure_And_Keeps_Healthy_Primary()
    {
        var failing = Link(TransportKind.AmneziaWG, _config, new[] { TransportKind.Direct });
        var (manager, _, _) = CreateManager(failing, amneziaWorks: false, directWorks: false);

        var failed = await manager.StartAllAsync();
        failed.Should().ContainSingle().Which.Status.Should().Be(LinkStatus.Failed);

        var healthy = Link(TransportKind.Direct);
        var (healthyManager, _, providerCalls) = CreateManager(healthy, amneziaWorks: false, directWorks: true);
        (await healthyManager.StartAsync(healthy.Id)).Status.Should().Be(LinkStatus.Ready);
        (await healthyManager.TryFailoverAsync(healthy.Id)).Status.Should().Be(LinkStatus.Ready);
        providerCalls().Should().Be(1, "a usable primary link is not restarted");

        var unknown = () => healthyManager.StartAsync(Guid.NewGuid());
        await unknown.Should().ThrowAsync<KeyNotFoundException>();
    }

    /// <summary>
    /// Verifies that an AmneziaWG link to a peer without an SSH server is checked on the TLS port of the peer, as a direct
    /// link is: nothing listens on its SSH port, and the tunnel would be reported as unusable for ever.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task AmneziaWgLink_Checks_The_Tls_Port_Of_A_Peer_Without_Ssh()
    {
        var peer = new PeerDescriptor(
            new NodeId("dest"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq",
            endpointMode: EndpointMode.Direct, tlsFingerprint: "SHA256:" + new string('B', 64), endpointPort: 18444);
        var config = new ChainLinkConfig(
            Guid.NewGuid(), "to-dest", TransportKind.AmneziaWG, peer, 0, 18444, 0,
            transportSettings: new Dictionary<string, string> { [AmneziaWgLink.ConfigPathSetting] = _config });
        var cli = Substitute.For<IAmneziaWgCli>();
        cli.GetStatusAsync(_config, Arg.Any<CancellationToken>()).Returns(new AmneziaWgStatus(true, "awg0", "10.8.0.2", null));
        _probe.CanConnectAsync("dest.internal", 18444, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        var link = new AmneziaWgLink(TestTexts.English, config, cli, _probe, TimeProvider.System);
        await link.StartAsync();

        link.CurrentState.Status.Should().Be(LinkStatus.Ready);
        await _probe.DidNotReceive().CanConnectAsync("dest.internal", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that stopping all links also stops a link whose configuration was removed while it was running: its
    /// tunnel must not stay up, and it must not keep the other links from being stopped.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkManager_Stops_A_Link_Whose_Configuration_Was_Removed()
    {
        var config = Link(TransportKind.Direct);
        var (manager, repository, link, _) = CreateManagerWithLink(config);
        await manager.StartAsync(config.Id);
        repository.GetAsync(config.Id, Arg.Any<CancellationToken>()).Returns((ChainLinkConfig?)null);

        var stopped = await manager.StopAllAsync();

        stopped.Should().ContainSingle().Which.Should().Match<LinkState>(state => state.Status == LinkStatus.Stopped && state.LinkName == config.Name);
        await link.Received(1).StopAsync(Arg.Any<CancellationToken>());
        (await manager.StopAllAsync()).Should().BeEmpty("nothing is running any more");
    }

    /// <summary>
    /// Verifies that releasing a link stops it when this manager started it, and touches nothing otherwise.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkManager_Release_Stops_Only_What_It_Started()
    {
        var config = Link(TransportKind.Direct);
        var (manager, _, link, provider) = CreateManagerWithLink(config);

        (await manager.ReleaseAsync(config.Id)).Should().BeFalse("the link was never started here");
        provider.DidNotReceiveWithAnyArgs().Create(default!);

        await manager.StartAsync(config.Id);
        (await manager.ReleaseAsync(config.Id)).Should().BeTrue();
        await link.Received(1).StopAsync(Arg.Any<CancellationToken>());
        (await manager.ReleaseAsync(config.Id)).Should().BeFalse();
    }

    /// <summary>
    /// Verifies that one link that fails to stop does not keep the manager from stopping the others, and that the failure
    /// is still reported.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task LinkManager_Stops_The_Other_Links_When_One_Fails_To_Stop()
    {
        var first = Link(TransportKind.Direct);
        var second = new ChainLinkConfig(Guid.NewGuid(), "to-relay", TransportKind.Direct, first.Peer, 0, 22, 1);
        var repository = Substitute.For<IChainLinkRepository>();
        var links = new Dictionary<Guid, ITransportLink>();
        foreach (var config in new[] { first, second })
        {
            repository.GetAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
            var link = Substitute.For<ITransportLink>();
            link.Kind.Returns(TransportKind.Direct);
            link.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LinkState(config.Id, config.Name, TransportKind.Direct, LinkStatus.Ready, "ok", DateTimeOffset.UtcNow));
            link.StopAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new SyncBridgeException("the tunnel service does not answer"));
            links[config.Id] = link;
        }

        var provider = Substitute.For<ITransportLinkProvider>();
        provider.Kind.Returns(TransportKind.Direct);
        provider.Create(Arg.Any<ChainLinkConfig>()).Returns(call => links[call.Arg<ChainLinkConfig>().Id]);
        var manager = new LinkManager(TestTexts.English, repository, new TransportLinkFactory(TestTexts.English, new[] { provider }), Substitute.For<IEventPublisher>(), TimeProvider.System);
        await manager.StartAsync(first.Id);
        await manager.StartAsync(second.Id);

        var stop = () => manager.StopAllAsync();

        await stop.Should().ThrowAsync<SyncBridgeException>();
        await links[first.Id].Received(1).StopAsync(Arg.Any<CancellationToken>());
        await links[second.Id].Received(1).StopAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Deletes temporary files.
    /// </summary>
    public void Dispose() => Directory.Delete(Path.GetDirectoryName(_config)!, recursive: true);

    /// <summary>
    /// Creates a chain link to Dest.
    /// </summary>
    /// <param name="transport">The primary transport.</param>
    /// <param name="configPath">The AmneziaWG configuration path, or <see langword="null"/>.</param>
    /// <param name="fallbacks">The fallback transports.</param>
    /// <returns>The link.</returns>
    private static ChainLinkConfig Link(TransportKind transport, string? configPath = null, IReadOnlyList<TransportKind>? fallbacks = null)
        => new(
            Guid.NewGuid(),
            "to-dest",
            transport,
            new PeerDescriptor(new NodeId("dest"), "Dest", NodeRole.Dest, "dest.internal", 22, "rcynq"),
            0,
            18443,
            0,
            fallbacks,
            configPath is null ? null : new Dictionary<string, string> { [AmneziaWgLink.ConfigPathSetting] = configPath });

    /// <summary>
    /// Creates a link manager whose Direct provider hands out one working link.
    /// </summary>
    /// <param name="config">The single configured link.</param>
    /// <returns>The manager, its repository, the link the provider hands out and the provider.</returns>
    private static (LinkManager Manager, IChainLinkRepository Repository, ITransportLink Link, ITransportLinkProvider Provider) CreateManagerWithLink(ChainLinkConfig config)
    {
        var repository = Substitute.For<IChainLinkRepository>();
        repository.GetAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { config });
        var link = Substitute.For<ITransportLink>();
        link.Kind.Returns(TransportKind.Direct);
        link.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(new LinkState(config.Id, config.Name, TransportKind.Direct, LinkStatus.Ready, "ok", DateTimeOffset.UtcNow));
        var provider = Substitute.For<ITransportLinkProvider>();
        provider.Kind.Returns(TransportKind.Direct);
        provider.Create(Arg.Any<ChainLinkConfig>()).Returns(link);
        var manager = new LinkManager(TestTexts.English, repository, new TransportLinkFactory(TestTexts.English, new[] { provider }), Substitute.For<IEventPublisher>(), TimeProvider.System);
        return (manager, repository, link, provider);
    }

    /// <summary>
    /// Creates a link manager with scripted AmneziaWG and Direct providers.
    /// </summary>
    /// <param name="config">The single configured link.</param>
    /// <param name="amneziaWorks">Whether AmneziaWG links start.</param>
    /// <param name="directWorks">Whether Direct links start.</param>
    /// <returns>The manager, the event publisher mock and a counter of created links.</returns>
    private static (LinkManager Manager, IEventPublisher Events, Func<int> CreatedLinks) CreateManager(ChainLinkConfig config, bool amneziaWorks, bool directWorks)
    {
        var created = 0;
        var repository = Substitute.For<IChainLinkRepository>();
        repository.GetAsync(config.Id, Arg.Any<CancellationToken>()).Returns(config);
        repository.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new[] { config });
        var events = Substitute.For<IEventPublisher>();

        ITransportLinkProvider Provider(TransportKind kind, bool works)
        {
            var provider = Substitute.For<ITransportLinkProvider>();
            provider.Kind.Returns(kind);
            provider.Create(Arg.Any<ChainLinkConfig>()).Returns(call =>
            {
                created++;
                var linkConfig = call.Arg<ChainLinkConfig>();
                var link = Substitute.For<ITransportLink>();
                link.Kind.Returns(kind);
                var state = new LinkState(linkConfig.Id, linkConfig.Name, kind, works ? LinkStatus.Ready : LinkStatus.Failed, works ? "ok" : $"{kind} broken", DateTimeOffset.UtcNow);
                link.GetStatusAsync(Arg.Any<CancellationToken>()).Returns(state);
                if (!works)
                {
                    link.StartAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new SyncBridgeException($"{kind} broken"));
                }

                return link;
            });
            return provider;
        }

        var factory = new TransportLinkFactory(TestTexts.English, new[] { Provider(TransportKind.AmneziaWG, amneziaWorks), Provider(TransportKind.Direct, directWorks) });
        return (new LinkManager(TestTexts.English, repository, factory, events, TimeProvider.System), events, () => created);
    }
}
