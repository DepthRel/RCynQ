using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Abstractions.Network;
using RCynQ.Core.Abstractions.Processes;
using RCynQ.Core.Config;
using RCynQ.Core.Data.Storage;
using RCynQ.Core.Enums;
using RCynQ.Core.Ids;
using RCynQ.Ssh.Keys;
using RCynQ.Ssh.Keys.HostKeys;
using RCynQ.Sync.Configuration;
using RCynQ.Sync.Configuration.Probing;

namespace RCynQ.Sync.Tests;

/// <summary>
/// Tests of <see cref="NodeEnvironmentProbe"/> with mocked interfaces, TCP connects and SSH tools.
/// </summary>
public sealed class NodeEnvironmentProbeTests : IDisposable
{
    /// <summary>A valid ed25519 public key blob.</summary>
    private const string KeyBlob = "AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl";

    /// <summary>The temporary data directory of the node.</summary>
    private readonly string _dataRoot = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "rcynq-probe-tests", Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>The node options.</summary>
    private readonly SyncBridgeOptions _options = new() { NodeId = "node-1", Role = NodeRole.Mediator, ListenPort = 18443 };

    /// <summary>The interface source substitute.</summary>
    private readonly INetworkInterfaceSource _interfaces = Substitute.For<INetworkInterfaceSource>();

    /// <summary>The TCP probe substitute.</summary>
    private readonly ITcpProbe _tcp = Substitute.For<ITcpProbe>();

    /// <summary>The process runner substitute.</summary>
    private readonly IProcessRunner _processes = Substitute.For<IProcessRunner>();

    /// <summary>
    /// Initializes a new instance of the <see cref="NodeEnvironmentProbeTests"/> class.
    /// </summary>
    public NodeEnvironmentProbeTests()
    {
        _interfaces.GetAddresses().Returns(new[]
        {
            new LocalNetworkAddress("127.0.0.1", "lo", false),
            new LocalNetworkAddress("192.168.1.10", "eth0", false),
            new LocalNetworkAddress("10.8.0.3", "awg0", true),
            new LocalNetworkAddress("169.254.1.1", "eth1", false),
            new LocalNetworkAddress("192.168.1.10", "eth0-alias", false)
        });
        _tcp.CanConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(false);
        _tcp.CanConnectAsync("10.8.0.3", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        _tcp.CanConnectAsync("127.0.0.1", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);
        _processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "ssh-keyscan"), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, $"# 127.0.0.1:22 SSH-2.0-OpenSSH\n127.0.0.1 ssh-ed25519 {KeyBlob}\n", string.Empty));
    }

    /// <summary>
    /// Verifies addresses, reachability, the SSH key from the data directory, host keys and warnings.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Reports_Environment()
    {
        var keys = Directory.CreateDirectory(AppDataPaths.FromRoot(_dataRoot).SshKeysDirectory).FullName;
        await File.WriteAllTextAsync(Path.Combine(keys, SshKeyLocator.DefaultKeyName + ".pub"), $"ssh-ed25519 {KeyBlob} rcynq:node-1\n");

        var report = await Create().ProbeAsync();

        report.Addresses.Select(address => address.Address).Should().Equal("10.8.0.3", "192.168.1.10");
        report.Addresses[0].Kind.Should().Be(NetworkAddressKind.Tunnel);
        report.Addresses[0].SshReachable.Should().BeTrue();
        report.SuggestedHost.Should().Be("10.8.0.3");
        report.SshdListening.Should().BeTrue();
        report.ServicePortListening.Should().BeFalse();
        report.SshPort.Should().Be(22);
        report.SshPublicKeyPath.Should().StartWith(_dataRoot);
        report.SshKeyFingerprint.Should().StartWith("SHA256:");
        report.HostKeyFingerprints.Should().ContainSingle().Which.Should().Be(report.SshKeyFingerprint);
        report.Warnings.Should().Equal(EnvironmentWarning.ServicePortNotListening);
    }

    /// <summary>
    /// Verifies the warnings when nothing is available.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Warns_Without_Sshd_And_Key()
    {
        _tcp.CanConnectAsync(default!, default, default, default).ReturnsForAnyArgs(false);

        var report = await Create().ProbeAsync();

        report.SshdListening.Should().BeFalse();
        report.SuggestedHost.Should().Be("10.8.0.3", "the tunnel address is still preferred when SSH does not answer anywhere yet");
        report.Warnings.Should().BeEquivalentTo(new[]
        {
            EnvironmentWarning.SshdNotListening,
            EnvironmentWarning.NoReachableAddress,
            EnvironmentWarning.NoSshPublicKey,
            EnvironmentWarning.ServicePortNotListening
        });
        await _processes.DidNotReceiveWithAnyArgs().RunAsync(default!, default);
    }

    /// <summary>
    /// Verifies that a node without an SSH server is checked on its TLS port and reports its certificate fingerprint.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Node_Without_Ssh_Server_Is_Checked_On_Its_Tls_Port()
    {
        _options.Endpoint = new EndpointOptions { Mode = EndpointMode.Direct, Port = 18444 };
        _tcp.CanConnectAsync("10.8.0.3", 18444, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(true);

        var report = await Create().ProbeAsync();

        report.EndpointMode.Should().Be(EndpointMode.Direct);
        report.EndpointPort.Should().Be(18444);
        report.TlsFingerprint.Should().StartWith("SHA256:").And.HaveLength(71);
        report.Addresses.Single(address => address.Address == "10.8.0.3").SshReachable.Should().BeTrue("reachability is checked on the TLS port");
        report.SshdListening.Should().BeFalse("no SSH server is expected on this node");
        report.HostKeyFingerprints.Should().BeEmpty();
        report.Warnings.Should().NotContain(new[] { EnvironmentWarning.SshdNotListening, EnvironmentWarning.NoReachableAddress, EnvironmentWarning.EndpointNotListening });
        await _tcp.DidNotReceive().CanConnectAsync("127.0.0.1", 22, Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());

        _tcp.CanConnectAsync(default!, default, default, default).ReturnsForAnyArgs(false);
        (await Create().ProbeAsync()).Warnings.Should().Contain(EnvironmentWarning.EndpointNotListening)
            .And.NotContain(EnvironmentWarning.SshdNotListening);
    }

    /// <summary>
    /// Verifies that the key is generated inside the data directory and recorded as <c>Ssh.IdentityFile</c>.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Generated_Key_Is_Stored_In_Data_Directory()
    {
        var config = Path.Combine(_dataRoot, "syncbridge.json");
        await File.WriteAllTextAsync(config, """{ "SyncBridge": { "NodeId": "node-1", "Role": "Mediator", "ssh": { "ConnectTimeoutSeconds": 5 } } }""");
        _processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "ssh-keygen" && spec.Arguments.Contains("-t")), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var arguments = call.Arg<ProcessSpec>().Arguments.ToList();
                var path = arguments[arguments.IndexOf("-f") + 1];
                File.WriteAllText(path, "PRIVATE");
                File.WriteAllText(path + ".pub", $"ssh-ed25519 {KeyBlob} rcynq:node-1");
                return new ProcessResult(0, string.Empty, string.Empty);
            });
        _processes.RunAsync(Arg.Is<ProcessSpec>(spec => spec.FileName == "ssh-keygen" && spec.Arguments.Contains("-l")), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(1, string.Empty, string.Empty));

        var report = await Create().GenerateSshKeyAsync(config);

        var expected = Path.Combine(AppDataPaths.FromRoot(_dataRoot).SshKeysDirectory, SshKeyLocator.DefaultKeyName);
        _options.Ssh.IdentityFile.Should().Be(expected);
        File.ReadAllText(expected).Should().Be("PRIVATE", "the owner keeps access after the permissions are restricted");
        report.SshPublicKeyPath.Should().Be(expected + ".pub");
        report.Warnings.Should().NotContain(EnvironmentWarning.NoSshPublicKey);
        var saved = await ConfigurationFileEditor.LoadAsync(config);
        saved.Ssh.IdentityFile.Should().Be(expected);
        saved.Ssh.ConnectTimeoutSeconds.Should().Be(5, "the existing Ssh section is updated instead of duplicated");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            Directory.Delete(_dataRoot, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort cleanup.
        }
    }

    /// <summary>
    /// Creates the probe under test.
    /// </summary>
    /// <returns>The probe.</returns>
    private NodeEnvironmentProbe Create()
        => new(
            new NodeConfig(new NodeId("node-1"), "Node", NodeRole.Mediator, null, null),
            _options,
            _interfaces,
            _tcp,
            new HostKeyVerifier(_processes, _options.Ssh),
            new SshKeyGenerator(_processes, _options.Ssh),
            new AppDataFactory(AppDataPaths.FromRoot(_dataRoot)),
            TimeProvider.System);
}
