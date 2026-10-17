using FluentAssertions;
using NSubstitute;
using RCynQ.Core.Api;
using RCynQ.Core.Enums;
using RCynQ.Ssh.Server;
using RCynQ.UI.Services.Api;
using RCynQ.UI.ViewModels.Dialogs;

namespace RCynQ.UI.Tests.Dialogs;

/// <summary>
/// Tests of the automatic detection in <see cref="ExportDialogViewModel"/>.
/// </summary>
public sealed class ExportDialogViewModelTests
{
    /// <summary>A host key fingerprint.</summary>
    private const string FINGERPRINT = "SHA256:47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU";

    /// <summary>An SSH public key line.</summary>
    private const string KEY = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIOMqqnkVzrm0SdG6UOoqKLsabgH5C9okWi0dh2l9GKJl rcynq:dev";

    /// <summary>The API substitute.</summary>
    private readonly IServiceApiClient _api = Substitute.For<IServiceApiClient>();

    /// <summary>
    /// Verifies that the probe report fills the form and offers every address, all of them included.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Prefills_Form()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY));
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api);

        await dialog.ProbeCommand.ExecuteAsync(null);

        dialog.Addresses.Select(option => option.Address).Should().Equal("10.8.0.3", "192.168.1.10");
        dialog.Addresses.Should().OnlyContain(option => option.IsIncluded);
        dialog.Addresses[0].KindText.Should().Be("VPN / туннель");
        dialog.ExportedAddresses.Should().Equal("10.8.0.3", "192.168.1.10");
        dialog.User.Should().Be("dev");
        dialog.SshPort.Should().Be(2222);
        dialog.SshdOk.Should().BeTrue();
        dialog.ServicePortOk.Should().BeFalse();
        dialog.ExportedSshPublicKey.Should().StartWith("ssh-ed25519 ");
        dialog.ExportedHostKeyFingerprint.Should().Be(FINGERPRINT);
        dialog.Warnings.Should().ContainSingle().Which.Should().Contain("Порт сервиса");
        await _api.DidNotReceive().GenerateSshKeyAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an address can be left out and that an address typed by hand comes first.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Addresses_Can_Be_Left_Out_And_Added_By_Hand()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY));
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api);
        await dialog.ProbeCommand.ExecuteAsync(null);

        dialog.Addresses[1].IsIncluded = false;
        dialog.Host = " relay.example.com ";

        dialog.ExportedAddresses.Should().Equal("relay.example.com", "10.8.0.3");
        dialog.ConfirmCommand.Execute(null);
        dialog.Confirmed.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that a missing key is created by itself, once, and the typed user name is kept.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Key_Is_Created_By_Itself()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: null));
        _api.GenerateSshKeyAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY));
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api) { User = "rcynq" };

        await dialog.ProbeCommand.ExecuteAsync(null);

        dialog.HasSshKey.Should().BeTrue();
        dialog.ExportedSshPublicKey.Should().StartWith("ssh-ed25519 ");
        dialog.User.Should().Be("rcynq");

        // A probe that still finds no key (the key went missing) does not create one again behind the user's back.
        await dialog.ProbeCommand.ExecuteAsync(null);
        await _api.Received(1).GenerateSshKeyAsync(Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Verifies that an export without any address is not confirmed.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Export_Needs_An_Address()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY));
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api);
        await dialog.ProbeCommand.ExecuteAsync(null);

        foreach (var option in dialog.Addresses)
        {
            option.IsIncluded = false;
        }

        dialog.ConfirmCommand.Execute(null);

        dialog.Confirmed.Should().BeFalse();
        dialog.Error.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that an unreachable service is reported and the export still needs an address.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Probe_Failure_Is_Shown()
    {
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns<NodeEnvironmentReport>(_ => throw new HttpRequestException("refused"));
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api);

        await dialog.ProbeCommand.ExecuteAsync(null);
        dialog.ConfirmCommand.Execute(null);

        dialog.HasProbeError.Should().BeTrue();
        dialog.IsProbing.Should().BeFalse();
        dialog.Confirmed.Should().BeFalse();
        dialog.Error.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies that an SSH server that does not answer can be installed from the dialog, and that a successful
    /// installation checks the node again.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Missing_Ssh_Server_Can_Be_Installed()
    {
        var setup = Substitute.For<IServerSetup>();
        setup.InstallSshServerAsync(Arg.Any<CancellationToken>()).Returns(new ServerSetupResult(ServerSetupOutcome.Completed, "ready"));
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY) with { SshdListening = false });
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api, setup);
        await dialog.ProbeCommand.ExecuteAsync(null);
        dialog.ServerSetup.CanInstallSshServer.Should().BeTrue();
        dialog.ServerSetup.CanOpenPort.Should().BeFalse();

        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY));
        await dialog.ServerSetup.InstallSshServerCommand.ExecuteAsync(null);

        dialog.ServerSetup.SetupMessage.Should().Be("ready");
        dialog.ServerSetup.IsSetupReady.Should().BeTrue();
        dialog.ServerSetup.HasManualCommands.Should().BeFalse();
        dialog.SshdOk.Should().BeTrue("the node was checked again");
        dialog.ServerSetup.CanInstallSshServer.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a failed installation shows the commands by hand and does not check the node again, and that the
    /// dialog offers nothing without the service that installs.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Failed_Installation_Shows_The_Commands_By_Hand()
    {
        var setup = Substitute.For<IServerSetup>();
        setup.InstallSshServerAsync(Arg.Any<CancellationToken>()).Returns(new ServerSetupResult(ServerSetupOutcome.Failed, "failed", "sudo apt install openssh-server"));
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(Report(sshKey: KEY) with { SshdListening = false });
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api, setup);
        await dialog.ProbeCommand.ExecuteAsync(null);

        await dialog.ServerSetup.InstallSshServerCommand.ExecuteAsync(null);

        dialog.ServerSetup.IsSetupReady.Should().BeFalse();
        dialog.ServerSetup.ManualCommands.Should().Be("sudo apt install openssh-server");
        await _api.Received(1).ProbeExportAsync(Arg.Any<CancellationToken>());

        var withoutSetup = new ExportDialogViewModel(TestTexts.Russian, _api);
        await withoutSetup.ProbeCommand.ExecuteAsync(null);
        withoutSetup.ServerSetup.CanInstallSshServer.Should().BeFalse();
    }

    /// <summary>
    /// Verifies that a TLS port that does not answer can be opened from the dialog, with the port of the node.
    /// </summary>
    /// <returns>A task representing the test.</returns>
    [Fact]
    public async Task Closed_Tls_Port_Can_Be_Opened()
    {
        var setup = Substitute.For<IServerSetup>();
        setup.OpenEndpointPortAsync(18444, Arg.Any<CancellationToken>()).Returns(new ServerSetupResult(ServerSetupOutcome.Completed, "open"));
        var closed = Report(sshKey: KEY) with
        {
            Addresses = new[] { new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, false) },
            EndpointMode = EndpointMode.Direct,
            EndpointPort = 18444
        };
        _api.ProbeExportAsync(Arg.Any<CancellationToken>()).Returns(closed);
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api, setup);
        await dialog.ProbeCommand.ExecuteAsync(null);

        dialog.ServerSetup.CanOpenPort.Should().BeTrue();
        dialog.ServerSetup.CanInstallSshServer.Should().BeFalse();
        dialog.ServerSetup.OpenPortText.Should().Contain("18444");
        await dialog.ServerSetup.OpenPortCommand.ExecuteAsync(null);

        await setup.Received(1).OpenEndpointPortAsync(18444, Arg.Any<CancellationToken>());
        dialog.ServerSetup.SetupMessage.Should().Be("open");
    }

    /// <summary>
    /// Verifies that a node without an SSH server shows its port and certificate instead of sshd details.
    /// </summary>
    [Fact]
    public void Node_Without_Ssh_Server_Shows_Tls_Details()
    {
        const string TLS = "SHA256:59CE0A214994CBC386DF73DA0F07AF6B19138641408A65ED2330C7FEA1BBFDF4";
        var dialog = new ExportDialogViewModel(TestTexts.Russian, _api);

        dialog.Apply(Report(sshKey: null) with
        {
            SshdListening = false,
            HostKeyFingerprints = Array.Empty<string>(),
            EndpointMode = EndpointMode.Direct,
            EndpointPort = 18444,
            TlsFingerprint = TLS
        });

        dialog.IsDirectEndpoint.Should().BeTrue();
        dialog.IsSshEndpoint.Should().BeFalse("the sshd host key section is hidden");
        dialog.TlsFingerprint.Should().Be(TLS);
        dialog.SshdOk.Should().BeTrue("the TLS port answers on 10.8.0.3");
        dialog.SshdStatus.Should().Contain("18444").And.Contain("без SSH-сервера");
        dialog.Addresses[0].StatusText.Should().Be("порт RCynQ отвечает");
        dialog.Addresses[1].StatusText.Should().Be("порт RCynQ не отвечает");
    }

    /// <summary>
    /// Creates a probe report.
    /// </summary>
    /// <param name="sshKey">The SSH public key line, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    private static NodeEnvironmentReport Report(string? sshKey)
        => new(
            "DEV-PC",
            "dev",
            2222,
            true,
            false,
            new[]
            {
                new NetworkAddressInfo("10.8.0.3", "awg0", NetworkAddressKind.Tunnel, true),
                new NetworkAddressInfo("192.168.1.10", "Ethernet", NetworkAddressKind.Private, false)
            },
            "10.8.0.3",
            sshKey,
            sshKey is null ? null : "/home/dev/.ssh/rcynq_ed25519.pub",
            sshKey is null ? null : "SHA256:key",
            new[] { FINGERPRINT },
            new[] { EnvironmentWarning.ServicePortNotListening },
            DateTimeOffset.UtcNow);
}
